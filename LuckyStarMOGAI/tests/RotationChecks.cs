using LuckyStar;

public static class RotationChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(string label, Action test)
        {
            test();
            count++;
            Console.WriteLine("PASS rotation: " + label);
        }
        static void Assert(bool ok, string reason = "rotation assertion failed")
        {
            if (!ok) throw new InvalidOperationException(reason);
        }
        static void Throws(Action action)
        {
            try { action(); }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("Expected rotation request to fail");
        }

        Check("AE keeps original on/off lifecycle without querying other providers", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.AE);
            session.SetCombat(true);
            session.SetCombat(true);
            session.SetCombat(false);
            session.End();
            Assert(api.AeChanges.SequenceEqual([true, false]));
            Assert(api.Calls.Count == 0);
        });
        Check("AE unload stops a running combat session once", () =>
        {
            var api = new FakeRotationApi();
            var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.AE);
            session.SetCombat(true);
            session.Dispose();
            session.Dispose();
            Assert(api.AeChanges.SequenceEqual([true, false]));
        });
        Check("missing AE commands fail before acquiring a session", () =>
        {
            var api = new FakeRotationApi { AeAvailable = false };
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.AE));
            Assert(!session.Begun && api.AeChanges.Count == 0 && api.Calls.Count == 0);
        });
        Check("Wrath initializes state before temporary configuration and scopes all writes to its lease", () =>
        {
            var api = new FakeRotationApi();
            var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.WrathCombo);
            var register = api.Calls.FindIndex(c => c.Call == RotationCall.WrathRegister);
            var state = api.Calls.FindIndex(c => c.Call == RotationCall.WrathState);
            var config = api.Calls.FindIndex(c => c.Call == RotationCall.WrathConfig);
            Assert(register >= 0 && register < state && state < config);
            Assert(api.Calls[state].Arguments[1] is false);
            Assert(api.Calls.Where(c => c.Call is RotationCall.WrathState or RotationCall.WrathConfig)
                .All(c => Equals(c.Arguments[0], api.Lease)));
            session.SetCombat(true);
            session.SetCombat(false);
            session.End();
            Assert(api.Count(RotationCall.WrathRegister) == 1 && api.Count(RotationCall.WrathRelease) == 1);
        });
        Check("Wrath manual-target and out-of-combat options use documented boxed IDs", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.WrathCombo);
            var options = api.Calls.Where(c => c.Call == RotationCall.WrathConfig)
                .ToDictionary(c => (int)c.Arguments[1], c => c.Arguments[2]);
            Assert(options.Count == 3);
            Assert(options[0] is false && options[13] is false && options[1] is int mode && mode == 0);
        });
        Check("Wrath false IPC readiness refuses registration and writes", () =>
        {
            var api = new FakeRotationApi { WrathReady = false };
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.WrathCombo));
            Assert(!session.Begun && api.Count(RotationCall.WrathRegister) == 0 && api.Count(RotationCall.WrathState) == 0);
        });
        Check("Wrath unconfigured job refuses without mutating job or global settings", () =>
        {
            var api = new FakeRotationApi { WrathJobReady = false };
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.WrathCombo));
            Assert(!session.Begun);
            Assert(api.Calls.All(c => c.Call is RotationCall.WrathReady or RotationCall.WrathJobReady));
        });
        Check("Wrath denied lease never writes config or releases a nonexistent lease", () =>
        {
            var api = new FakeRotationApi { GrantLease = false };
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.WrathCombo));
            session.End();
            Assert(api.Count(RotationCall.WrathRegister) == 1 && api.Count(RotationCall.WrathState) == 0 &&
                   api.Count(RotationCall.WrathConfig) == 0 && api.Count(RotationCall.WrathRelease) == 0);
        });
        Check("Wrath initial state failure releases acquired lease", () =>
        {
            var api = new FakeRotationApi { WrathStateResult = 10 };
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.WrathCombo));
            Assert(!session.Begun && api.Count(RotationCall.WrathRelease) == 1 && api.Count(RotationCall.WrathConfig) == 0);
        });
        Check("Wrath partial config failure releases without leaking a callback registration", () =>
        {
            var api = new FakeRotationApi { FailConfigOption = 13 };
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.WrathCombo));
            Assert(!session.Begun && api.Count(RotationCall.WrathRelease) == 1);
            Assert(api.LeaseLostSubscribers == 0);
        });
        Check("Wrath release exception during Begin cleanup unsubscribes and retains the lease for End retry", () =>
        {
            var api = new FakeRotationApi { FailConfigOption = 13, ReleaseFailuresRemaining = 1 };
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.WrathCombo));
            Assert(!session.Begun && api.LeaseLostSubscribers == 0);
            Assert(api.Count(RotationCall.WrathRelease) == 1);
            Throws(() => session.Begin(RotationProvider.AE));
            session.End();
            Assert(api.Count(RotationCall.WrathRelease) == 2);
            Assert(api.Calls.Where(c => c.Call == RotationCall.WrathRelease)
                .All(c => Equals(c.Arguments[0], api.Lease)), "Cleanup retry must use the same acquired lease");
            session.Begin(RotationProvider.AE);
            session.End();
            Assert(api.Count(RotationCall.WrathRegister) == 1 && api.Count(RotationCall.WrathRelease) == 2);
        });
        Check("Wrath release exception during End blocks new sessions until a successful release retry", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.WrathCombo);
            session.SetCombat(true);
            api.ReleaseFailuresRemaining = 1;
            Throws(session.End);
            Assert(!session.Begun && api.LeaseLostSubscribers == 0);
            Assert(api.Count(RotationCall.WrathRelease) == 1);
            Throws(() => session.Begin(RotationProvider.WrathCombo));
            Throws(() => session.Begin(RotationProvider.Prome, true));
            Assert(api.Count(RotationCall.WrathRegister) == 1 && api.Count(RotationCall.PromeStart) == 0);
            session.End();
            Assert(api.Count(RotationCall.WrathRelease) == 2);
            Assert(api.Calls.Where(c => c.Call == RotationCall.WrathRelease)
                .All(c => Equals(c.Arguments[0], api.Lease)));
            session.Begin(RotationProvider.WrathCombo);
            Assert(session.CheckWhileReady() == null);
            session.End();
            Assert(api.Count(RotationCall.WrathRegister) == 2 && api.Count(RotationCall.WrathRelease) == 3);
        });
        Check("Wrath combat-enable rejection is cleaned up by farm-stop End", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.WrathCombo);
            api.WrathStateResult = 11;
            Throws(() => session.SetCombat(true));
            session.End();
            Assert(!session.Begun && api.Count(RotationCall.WrathRelease) == 1);
        });
        Check("Wrath accepted asynchronous and duplicate results are not errors", () =>
        {
            var api = new FakeRotationApi { WrathStateResult = 13, WrathConfigResult = 1 };
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.WrathCombo);
            session.SetCombat(true);
            Assert(session.Begun);
        });
        Check("Wrath revocation callback defers stop and never reenters ReleaseControl or reacquires", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.WrathCombo);
            session.SetCombat(true);
            var callsBefore = api.Calls.Count;
            api.RaiseLeaseLost(0, "user revoked");
            Assert(api.Calls.Count == callsBefore, "Callback must not call IPC recursively");
            Assert(session.CheckWhileReady() is { Length: > 0 });
            Throws(() => session.SetCombat(false));
            session.End();
            Assert(api.Count(RotationCall.WrathRegister) == 1 && api.Count(RotationCall.WrathRelease) == 0);
        });
        Check("Wrath own-release callback is ignored and event subscription is removed", () =>
        {
            var api = new FakeRotationApi();
            var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.WrathCombo);
            session.End();
            session.End();
            Assert(api.Count(RotationCall.WrathRelease) == 1 && api.LeaseLostSubscribers == 0);
            session.Begin(RotationProvider.WrathCombo);
            Assert(session.CheckWhileReady() == null);
            session.End();
        });
        Check("missing Wrath gate refuses before any mutation", () =>
        {
            var api = new FakeRotationApi();
            api.Missing.Add(RotationCall.WrathRelease);
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.WrathCombo));
            Assert(api.Calls.Count == 0 && !session.Begun);
        });
        Check("already running RSR is borrowed and never stopped", () =>
        {
            var api = new FakeRotationApi { RsrActive = true };
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.RotationSolverReborn);
            session.SetCombat(true);
            session.SetCombat(false);
            session.End();
            Assert(api.Count(RotationCall.RsrMode) == 0 && api.RsrActive);
        });
        Check("already running Prome is borrowed and never stopped", () =>
        {
            var api = new FakeRotationApi { PromeActive = true };
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.Prome);
            session.SetCombat(true);
            session.SetCombat(false);
            session.End();
            Assert(api.Count(RotationCall.PromeStart) == 0 && api.Count(RotationCall.PromeStop) == 0 && api.PromeActive);
        });
        Check("inactive RSR cannot be started without explicit allowStart", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.RotationSolverReborn));
            session.End();
            Assert(api.Count(RotationCall.RsrMode) == 0);
        });
        Check("inactive Prome cannot be started without explicit allowStart", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.Prome));
            session.End();
            Assert(api.Count(RotationCall.PromeStart) == 0 && api.Count(RotationCall.PromeStop) == 0);
        });
        Check("owned RSR starts in target-preserving Henched mode and closes once", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.RotationSolverReborn, true);
            session.SetCombat(true);
            session.SetCombat(false);
            session.End();
            Assert(api.Calls.Where(c => c.Call == RotationCall.RsrMode)
                .Select(c => (string)c.Arguments[0]).SequenceEqual(["Henched", "Off"]));
        });
        Check("owned Prome starts and stops exactly once", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.Prome, true);
            session.SetCombat(true);
            session.End();
            session.End();
            Assert(api.Count(RotationCall.PromeStart) == 1 && api.Count(RotationCall.PromeStop) == 1 && !api.PromeActive);
        });
        Check("Prome Start false conveys no ownership and never calls Stop", () =>
        {
            var api = new FakeRotationApi { PromeStartResult = false };
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.Prome, true);
            Throws(() => session.SetCombat(true));
            session.End();
            Assert(api.Count(RotationCall.PromeStart) == 1 && api.Count(RotationCall.PromeStop) == 0);
        });
        Check("Prome accepted Start but inactive getter is cleaned up", () =>
        {
            var api = new FakeRotationApi { ActivateOnStart = false };
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.Prome, true);
            Throws(() => session.SetCombat(true));
            session.End();
            Assert(api.Count(RotationCall.PromeStart) == 1 && api.Count(RotationCall.PromeStop) == 1);
        });
        Check("RSR accepted start but inactive getter is cleaned up", () =>
        {
            var api = new FakeRotationApi { ActivateOnStart = false };
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.RotationSolverReborn, true);
            Throws(() => session.SetCombat(true));
            session.End();
            Assert(api.Calls.Where(c => c.Call == RotationCall.RsrMode)
                .Select(c => (string)c.Arguments[0]).SequenceEqual(["Henched", "Off"]));
        });
        Check("Prome missing ACR refuses startup before switching rotation", () =>
        {
            var api = new FakeRotationApi { Acr = "  " };
            using var session = new CombatRotationSession(api);
            Throws(() => session.Begin(RotationProvider.Prome, true));
            Assert(!session.Begun && api.Count(RotationCall.PromeStart) == 0);
        });
        Check("missing RSR or Prome gates never fall back to another engine", () =>
        {
            foreach (var provider in new[] { RotationProvider.RotationSolverReborn, RotationProvider.Prome })
            {
                var api = new FakeRotationApi();
                api.Missing.Add(provider == RotationProvider.Prome ? RotationCall.PromeStop : RotationCall.RsrMode);
                using var session = new CombatRotationSession(api);
                Throws(() => session.Begin(provider, true));
                Assert(api.Calls.Count == 0 && api.AeChanges.Count == 0);
            }
        });
        Check("owned RSR inactive state never restarts and is closed once on cleanup", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.RotationSolverReborn, true);
            session.SetCombat(true);
            api.RsrActive = false;
            Assert(session.CheckWhileReady() is { Length: > 0 });
            session.End();
            Assert(api.Calls.Where(c => c.Call == RotationCall.RsrMode)
                .Select(c => (string)c.Arguments[0]).SequenceEqual(["Henched", "Off"]));
        });
        Check("owned Prome inactive state never restarts and is closed once on cleanup", () =>
        {
            var api = new FakeRotationApi();
            using var session = new CombatRotationSession(api);
            session.Begin(RotationProvider.Prome, true);
            session.SetCombat(true);
            api.PromeActive = false;
            Assert(session.CheckWhileReady() is { Length: > 0 });
            session.End();
            Assert(api.Count(RotationCall.PromeStart) == 1 && api.Count(RotationCall.PromeStop) == 1);
        });
        Check("borrowed RSR and Prome inactive states are reported without closing user-owned switches", () =>
        {
            foreach (var provider in new[] { RotationProvider.RotationSolverReborn, RotationProvider.Prome })
            {
                var api = new FakeRotationApi { RsrActive = true, PromeActive = true };
                using var session = new CombatRotationSession(api);
                session.Begin(provider);
                session.SetCombat(true);
                api.RsrActive = api.PromeActive = false;
                Assert(session.CheckWhileReady() is { Length: > 0 });
                session.End();
                Assert(api.Count(RotationCall.RsrMode) == 0 && api.Count(RotationCall.PromeStart) == 0 &&
                       api.Count(RotationCall.PromeStop) == 0);
            }
        });
        return count;
    }

    internal sealed class FakeRotationApi : IRotationApi
    {
        public readonly record struct Invocation(RotationCall Call, object[] Arguments);
        public List<Invocation> Calls { get; } = [];
        public List<bool> AeChanges { get; } = [];
        public HashSet<RotationCall> Missing { get; } = [];
        public bool AeAvailable { get; set; } = true;
        public bool RsrActive, PromeActive;
        public bool WrathReady = true, WrathJobReady = true, GrantLease = true;
        public bool PromeStartResult = true, PromeStopResult = true, ActivateOnStart = true;
        public int WrathStateResult, WrathConfigResult;
        public int ReleaseFailuresRemaining;
        public int? FailConfigOption;
        public string Acr = "Configured ACR";
        public Guid Lease { get; } = Guid.Parse("89ad5cbc-658a-4b5e-8ba7-570b3a26d5b6");
        private Action<int, string>? leaseLost;
        public int LeaseLostSubscribers => leaseLost?.GetInvocationList().Length ?? 0;
        public event Action<int, string>? LeaseLost { add => leaseLost += value; remove => leaseLost -= value; }
        public void RaiseLeaseLost(int reason, string info) => leaseLost?.Invoke(reason, info);
        public bool Has(RotationCall call) => !Missing.Contains(call);
        public void SetAe(bool enabled) => AeChanges.Add(enabled);
        public int Count(RotationCall call) => Calls.Count(c => c.Call == call);

        public object? Invoke(RotationCall call, params object[] arguments)
        {
            if (!Has(call)) throw new InvalidOperationException("Called missing IPC gate");
            Calls.Add(new(call, [.. arguments]));
            switch (call)
            {
                case RotationCall.RsrActive: return RsrActive;
                case RotationCall.RsrMode:
                    RsrActive = (string)arguments[0] == "Henched" && ActivateOnStart;
                    return null;
                case RotationCall.WrathReady: return WrathReady;
                case RotationCall.WrathJobReady: return WrathJobReady;
                case RotationCall.WrathRegister: return GrantLease ? Lease : null;
                case RotationCall.WrathState: return WrathStateResult;
                case RotationCall.WrathConfig:
                    return (int)arguments[1] == FailConfigOption ? 15 : WrathConfigResult;
                case RotationCall.WrathRelease:
                    if (ReleaseFailuresRemaining > 0)
                    {
                        ReleaseFailuresRemaining--;
                        throw new InvalidOperationException("Simulated IPC release failure before native lease deletion");
                    }
                    RaiseLeaseLost(3, "native callback runs before lease deletion");
                    return null;
                case RotationCall.PromeAcr: return Acr;
                case RotationCall.PromeActive: return PromeActive;
                case RotationCall.PromeStart:
                    if (PromeStartResult && ActivateOnStart) PromeActive = true;
                    return PromeStartResult;
                case RotationCall.PromeStop:
                    if (PromeStopResult) PromeActive = false;
                    return PromeStopResult;
                default: throw new InvalidOperationException("Unexpected IPC operation");
            }
        }
    }
}
