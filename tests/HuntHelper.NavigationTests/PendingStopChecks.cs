using HuntHelper.Utilities;

namespace HuntHelper.NavigationTests;

internal static class PendingStopChecks
{
    private sealed class AsyncMoveRequest { public int Calls; public void Stop() => Calls++; }
    private sealed class NativeCommands(AsyncMoveRequest move) { private readonly AsyncMoveRequest asyncMove = move; }
    private static void Check(bool ok) { if (!ok) throw new Exception("Pending-stop compatibility assertion failed"); }
    public static void Run(Action<string, Action> run)
    {
        run("Loaded CN async-move public Stop resolves from its real command target", () =>
        {
            var native = new AsyncMoveRequest(); var stop = VnavmeshPendingStop.Resolve(new NativeCommands(native));
            Check(stop != null && native.Calls == 0); stop!(); Check(native.Calls == 1);
        });
        run("Only exact vnav stop commands match without capturing unrelated chat", () =>
        {
            foreach (var t in new[] { "/vnav stop", " /vnav   stop ", "/VNAV STOP" }) Check(VnavmeshPendingStop.IsStopCommand(t));
            foreach (var t in new[] { "/vnav moveto 1 2 3", "/p /vnav stop", "/vnav stoplater", "vnav stop", "/vnav stop now" }) Check(!VnavmeshPendingStop.IsStopCommand(t));
        });
        run("HHT stop invokes complete native cancellation before executor-only Path.Stop", () =>
        {
            var cancelled = 0; using var f = new Fixture(() => () => cancelled++);
            Check(f.Manager.TryMoveTo(f.Mob)); f.Manager.Stop(); Check(cancelled == 1 && f.StopRequests == 1 && !f.Manager.OwnsMovement);
        });
        run("Observed vnav stop cancels owned pending work and signals patrol interruption", () =>
        {
            var cancelled = 0; using var f = new Fixture(() => () => cancelled++);
            Check(f.Manager.TryMoveTo(f.Mob)); f.Manager.NotifyStopCommand(); f.Framework.Tick();
            Check(cancelled == 1 && f.Manager.Interrupted && !f.Manager.OwnsMovement && f.MoveRequests == 1);
        });
        run("Unavailable full cancellation fails closed without starting a route", () =>
        { using var f = new Fixture(() => null); Check(!f.Manager.TryMoveTo(f.Mob) && f.MoveRequests == 0); });
        run("Unowned vnav stop is passed through without touching another plugin route", () =>
        { var cancelled = 0; using var f = new Fixture(() => () => cancelled++); f.Running = true; f.Manager.NotifyStopCommand(); Check(cancelled == 0 && f.StopRequests == 0 && f.Running); });
    }
}
