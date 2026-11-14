// Offline API boundary checks only. Creates private channels without registering global IPC,
// constructing plugins, requesting a game service, or executing game/plugin commands.
using Dalamud.Plugin.Ipc;
using System.Reflection;
using System.Runtime.CompilerServices;

var passed = 0;
void Check(string label, Action run)
{
    run(); passed++; Console.WriteLine("PASS " + label);
}
void Require(bool condition, string message = "IPC contract mismatch")
{
    if (!condition) throw new InvalidOperationException(message);
}

Check("RSR Henched string reaches older byte enum by name", () =>
{
    var observed = LegacyRsr.Off;
    var ipc = PrivateIpc.Subscriber<ICallGateSubscriber<string, object>>((Action<LegacyRsr>)(v => observed = v), action: true);
    Require(ipc.HasAction);
    ipc.InvokeAction("Henched");
    Require(observed == LegacyRsr.Henched && (byte)observed == 4);
});
Check("RSR Henched string reaches newer byte enum despite shifted ordinals", () =>
{
    var observed = CurrentRsr.Off;
    var ipc = PrivateIpc.Subscriber<ICallGateSubscriber<string, object>>((Action<CurrentRsr>)(v => observed = v), action: true);
    ipc.InvokeAction("Henched");
    Require(observed == CurrentRsr.Henched && (byte)observed == 5);
});
Check("RSR Off string clears either enum layout", () =>
{
    var older = LegacyRsr.Henched; var newer = CurrentRsr.Henched;
    PrivateIpc.Subscriber<ICallGateSubscriber<string, object>>((Action<LegacyRsr>)(v => older = v), true).InvokeAction("Off");
    PrivateIpc.Subscriber<ICallGateSubscriber<string, object>>((Action<CurrentRsr>)(v => newer = v), true).InvokeAction("Off");
    Require(older == LegacyRsr.Off && newer == CurrentRsr.Off);
});
Check("RSR Manual string selects Manual in both layouts", () =>
{
    var older = LegacyRsr.Off; var newer = CurrentRsr.Off;
    PrivateIpc.Subscriber<ICallGateSubscriber<string, object>>((Action<LegacyRsr>)(v => older = v), true).InvokeAction("Manual");
    PrivateIpc.Subscriber<ICallGateSubscriber<string, object>>((Action<CurrentRsr>)(v => newer = v), true).InvokeAction("Manual");
    Require(older == LegacyRsr.Manual && newer == CurrentRsr.Manual);
});
Check("Wrath SetAutoRotationState enum return converts to int through real generic subscriber", () =>
{
    var lease = Guid.NewGuid(); var seen = false;
    var ipc = PrivateIpc.Subscriber<ICallGateSubscriber<Guid, bool, int>>(
        (Func<Guid, bool, WrathResult>)((id, on) => { Require(id == lease && on); seen = true; return WrathResult.Okay; }));
    Require(ipc.HasFunction && ipc.InvokeFunc(lease, true) == 0 && seen);
});
Check("Wrath nonzero and negative enum results retain exact error codes", () =>
{
    foreach (var result in Enum.GetValues<WrathResult>())
    {
        var ipc = PrivateIpc.Subscriber<ICallGateSubscriber<Guid, bool, int>>(
            (Func<Guid, bool, WrathResult>)((_, _) => result));
        Require(ipc.InvokeFunc(Guid.NewGuid(), false) == (int)result);
    }
});
Check("Wrath object config option/int value reaches provider with numeric semantics", () =>
{
    var lease = Guid.NewGuid(); var seen = false;
    var ipc = PrivateIpc.Subscriber<ICallGateSubscriber<Guid, object, object, int>>(
        (Func<Guid, object, object, WrathResult>)((id, option, value) =>
        {
            // Actual Wrath provider accepts object/object and converts numeric values itself.
            Require(id == lease && Convert.ToInt32(option) == 1);
            var mode = (DpsMode)Enum.ToObject(typeof(DpsMode), value);
            Require(mode == DpsMode.Manual);
            seen = true; return WrathResult.OkayWorking;
        }));
    Require(ipc.InvokeFunc(lease, 1, 0) == 1 && seen);
});
Check("Wrath object config bool value retains bool semantics", () =>
{
    foreach (var enabled in new[] { false, true })
    {
        var ipc = PrivateIpc.Subscriber<ICallGateSubscriber<Guid, object, object, int>>(
            (Func<Guid, object, object, WrathResult>)((_, option, value) =>
            {
                Require(Convert.ToInt32(option) == 13 && value is bool && Convert.ToBoolean(value) == enabled);
                return WrathResult.Okay;
            }));
        Require(ipc.InvokeFunc(Guid.NewGuid(), 13, enabled) == 0);
    }
});
Check("Wrath nullable Guid registration result returns an actual lease", () =>
{
    var lease = Guid.NewGuid(); var seen = false;
    var ipc = PrivateIpc.Subscriber<ICallGateSubscriber<string, string, string, Guid?>>(
        (Func<string, string, string, Guid?>)((plugin, name, callback) =>
        { Require(plugin == "LuckyStarMOGAI" && name == "test" && callback == "private-test"); seen = true; return lease; }));
    Require(ipc.InvokeFunc("LuckyStarMOGAI", "test", "private-test") == lease && seen);
});
Check("Wrath nullable Guid registration result preserves null rejection", () =>
{
    var ipc = PrivateIpc.Subscriber<ICallGateSubscriber<string, string, string, Guid?>>(
        (Func<string, string, string, Guid?>)((_, _, _) => null));
    Require(ipc.InvokeFunc("LuckyStarMOGAI", "test", "private-test") is null);
});
Check("Wrath lease cancellation enum converts to int callback", () =>
{
    var observed = -1; var text = "";
    var callback = PrivateIpc.Subscriber<ICallGateSubscriber<LeaseCancellation, string, object>>(
        (Action<int, string>)((reason, info) => { observed = reason; text = info; }), true);
    callback.InvokeAction(LeaseCancellation.UserRevoked, "user revoked control");
    Require(observed == 2 && text == "user revoked control");
});
Console.WriteLine($"ALL {passed} OFFLINE SDK IPC CHECKS PASSED");

static class PrivateIpc
{
    // Skip the pubsub constructor specifically because it would ask Dalamud's global service
    // registry for a channel. Inject a freshly constructed, isolated channel into the real
    // installed generic pubsub implementation, then call the public subscriber interface.
    public static T Subscriber<T>(Delegate provider, bool action = false) where T : class
    {
        var assembly = typeof(ICallGateSubscriber<>).Assembly;
        var channelType = assembly.GetType("Dalamud.Plugin.Ipc.Internal.CallGateChannel", throwOnError: true)!;
        var channel = Activator.CreateInstance(channelType,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null,
            ["LuckyStarMOGAI.private.offline." + Guid.NewGuid()], null)!;
        channelType.GetProperty(action ? "Action" : "Func")!.SetValue(channel, provider);
        var arguments = typeof(T).GetGenericArguments();
        var subscriberType = assembly.GetType($"Dalamud.Plugin.Ipc.Internal.CallGatePubSub`{arguments.Length}", throwOnError: true)!
            .MakeGenericType(arguments);
        var subscriber = RuntimeHelpers.GetUninitializedObject(subscriberType);
        subscriberType.BaseType!.GetProperty("Channel", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(subscriber, channel);
        return (T)subscriber;
    }
}

// Representative local enum layouts, never types loaded from or calls sent to RSR/Wrath.
enum LegacyRsr : byte { Off = 0, Auto = 1, Manual = 2, AutoDuty = 3, Henched = 4, PvP = 5 }
enum CurrentRsr : byte { Off = 0, Auto = 1, TargetOnly = 2, Manual = 3, AutoDuty = 4, Henched = 5, PvP = 6 }
enum WrathResult { Ignored = -1, Okay = 0, OkayWorking = 1, IpcDisabled = 10, InvalidLease = 11, BlacklistedLease = 12, Duplicate = 13, PlayerNotAvailable = 14, InvalidConfiguration = 15, InvalidValue = 16 }
enum DpsMode { Manual = 0, HighestMax = 1, LowestMax = 2, HighestCurrent = 3, LowestCurrent = 4, TankTarget = 5, Nearest = 6, Furthest = 7 }
enum LeaseCancellation { Disabled = 0, Disposed = 1, UserRevoked = 2 }
