using System.Numerics;
using LuckyStar;

internal static class DeathRecoveryChecks
{
    public static int Run()
    {
        var count = 0;
        void Check(string title, Action test)
        {
            test();
            count++;
            Console.WriteLine("PASS death recovery: " + title);
        }

        static void Assert(bool result)
        {
            if (!result) throw new Exception("death recovery assertion failed");
        }

        Check("ordinary farming and map loading do not enter death recovery", () =>
        {
            var flow = new Scenario();
            var scope = Scenario.Destination;
            Assert(FarmDeathRecovery.CanBegin(flow.Context, scope));
            Assert(!FarmDeathRecovery.CanBegin(flow.Context with { Dead = false, Combat = true }, scope));
            Assert(!FarmDeathRecovery.CanBegin(flow.Context with { Loading = true, Ready = false }, scope));
            Assert(!FarmDeathRecovery.CanBegin(flow.Context with { LoggedIn = false }, scope));
            Assert(!FarmDeathRecovery.CanBegin(flow.Context with { Scope = scope with { Instance = 2 } }, scope));
        });

        Check("return once, teleport once and wait for the original map and navigation", () =>
        {
            var flow = new Scenario();
            Assert(flow.Step(0) == RecoveryAction.Waiting);
            Assert(flow.Step(1000) == RecoveryAction.Waiting && flow.Returns == 1 && flow.Teleports == 0);
            flow.Context = flow.Context with { Loading = true, Ready = false, Scope = default };
            Assert(flow.Step(1500) == RecoveryAction.Waiting);
            flow.ReviveAt(129);
            Assert(flow.Step(2000) == RecoveryAction.Waiting && flow.Teleports == 1);
            Assert(flow.Step(5000) == RecoveryAction.Waiting && flow.Teleports == 1);
            flow.ReviveAt(Scenario.Destination.Territory);
            flow.NavReady = false;
            Assert(flow.Step(5500) == RecoveryAction.Waiting);
            Assert(flow.Step(6300) == RecoveryAction.Waiting);
            flow.NavReady = true;
            Assert(flow.Step(6350) == RecoveryAction.Resume && flow.Returns == 1 && flow.Teleports == 1);
        });

        Check("same-map revival continues once ready without paying for redundant travel", () =>
        {
            var flow = new Scenario();
            flow.Step(0);
            flow.ReviveAt(Scenario.Destination.Territory);
            Assert(flow.Step(2000) == RecoveryAction.Waiting);
            Assert(flow.Step(2750) == RecoveryAction.Resume && flow.Teleports == 0);
        });

        Check("same-map teleport back to Ruby Sea's farm area waits for its own loading transition", () =>
        {
            var flow = new Scenario();
            flow.Step(0);
            flow.ReviveAt(Scenario.Destination.Territory);
            Assert(flow.Step(1000, inFarmArea: false) == RecoveryAction.Waiting && flow.Teleports == 1);
            Assert(flow.Step(2000) == RecoveryAction.Waiting);
            flow.Context = flow.Context with { Loading = true, Ready = false };
            Assert(flow.Step(3000) == RecoveryAction.Waiting);
            flow.ReviveAt(Scenario.Destination.Territory);
            Assert(flow.Step(4000) == RecoveryAction.Waiting);
            Assert(flow.Step(4750) == RecoveryAction.Resume && flow.Teleports == 1);
            Assert(RubyCaves.CanStartPatrol(flow.Context with { Position = RubyCaves.Tamamizu.ScanPoint }));
            Assert(!RubyCaves.CanStartPatrol(flow.Context with { Position = Vector3.Zero }));
        });

        Check("a respawn back on another server cannot trigger teleport or farming", () =>
        {
            var flow = new Scenario();
            flow.Step(0);
            flow.ReviveAt(129, world: 1184);
            Assert(flow.Step(1000) == RecoveryAction.Failed && flow.Teleports == 0);
        });

        Check("a different instance never resumes the saved run", () =>
        {
            var flow = new Scenario();
            flow.Step(0);
            flow.ReviveAt(129);
            flow.Step(1000);
            flow.ReviveAt(Scenario.Destination.Territory, instance: 3);
            Assert(flow.Step(2000) == RecoveryAction.Waiting);
            Assert(flow.Step(2800) == RecoveryAction.Failed && flow.Teleports == 1);
        });

        Check("loading, busy and casting prevent requests and reset the readiness wait", () =>
        {
            var flow = new Scenario();
            flow.Context = flow.Context with { Loading = true, Ready = false };
            Assert(flow.Step(0) == RecoveryAction.Waiting && flow.Returns == 0);
            flow.ReviveAt(129);
            flow.Context = flow.Context with { Casting = true };
            Assert(flow.Step(500) == RecoveryAction.Waiting && flow.Teleports == 0);
            flow.ReviveAt(129);
            flow.Step(1000);
            flow.ReviveAt(Scenario.Destination.Territory);
            flow.Step(2000);
            flow.Context = flow.Context with { Busy = true };
            Assert(flow.Step(2750) == RecoveryAction.Waiting);
            flow.ReviveAt(Scenario.Destination.Territory);
            Assert(flow.Step(2800) == RecoveryAction.Waiting);
            Assert(flow.Step(3550) == RecoveryAction.Resume);
        });

        Check("a rejected or unavailable teleport ends recovery", () =>
        {
            var flow = new Scenario { TeleportError = "原地图没有已解锁的传送水晶" };
            flow.Step(0);
            flow.ReviveAt(129);
            Assert(flow.Step(1000) == RecoveryAction.Failed && flow.Teleports == 1);
            Assert(flow.Recovery.State == flow.TeleportError);
        });

        Check("wrong-map arrival stops without submitting another teleport", () =>
        {
            var flow = new Scenario();
            flow.Step(0);
            flow.ReviveAt(129);
            flow.Step(1000);
            flow.ReviveAt(130);
            Assert(flow.Step(2000) == RecoveryAction.Failed && flow.Teleports == 1);
        });

        Check("missing revive prompt, missing loading transition and logout are bounded", () =>
        {
            var noPrompt = new Scenario { CanReturn = false };
            Assert(noPrompt.Step(0) == RecoveryAction.Waiting);
            Assert(noPrompt.Step(120_000) == RecoveryAction.Failed && noPrompt.Teleports == 0);
            var noArrival = new Scenario();
            noArrival.Step(0);
            noArrival.ReviveAt(129);
            noArrival.Step(1000);
            Assert(noArrival.Step(120_000) == RecoveryAction.Failed && noArrival.Teleports == 1);
            var loggedOut = new Scenario();
            loggedOut.Context = loggedOut.Context with { LoggedIn = false, Ready = false };
            Assert(loggedOut.Step(200) == RecoveryAction.Failed && loggedOut.Returns == 0);
        });

        Check("recovery reports use the original S watch without accepting cross-world sightings", () =>
        {
            var now = DateTimeOffset.UtcNow;
            var watch = new SSpawnWatch(1, Scenario.Destination, 371, 5984, now.AddSeconds(-1));
            var signal = new SSpawnSignal(1, Scenario.Destination, 5984, now, "Sonar");
            Assert(SSpawnStopRules.MatchesWatch(watch, signal, now));
            Assert(!SSpawnStopRules.MatchesWatch(watch, signal with { Scope = new(1184, 613, 0) }, now));
            Assert(!SSpawnStopRules.MatchesWatch(watch, signal with { Scope = new(1183, 613, 1) }, now));
        });
        return count;
    }

    private sealed class Scenario
    {
        public static readonly FarmScope Destination = new(1183, 613, 0);
        public FarmContext Context = new(true, Destination, Vector3.Zero, true, false, false, false, false, false);
        public FarmDeathRecovery Recovery = new(Destination, 0);
        public int Returns, Teleports;
        public bool NavReady = true, CanReturn = true;
        public string? TeleportError;

        public RecoveryAction Step(long now, bool inFarmArea = true) => Recovery.Tick(Context, now, inFarmArea,
            () => { Returns++; return CanReturn; }, () => { Teleports++; return TeleportError; }, () => NavReady);

        public void ReviveAt(uint territory, uint world = 1183, uint instance = 0) =>
            Context = new(true, new(world, territory, instance), Vector3.Zero, false, false, false, false, false, false);
    }
}
