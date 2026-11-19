using HuntHelper.Gui.Native;
using System.Numerics;

internal static class NativeWindowChecks
{
    internal static readonly (string Name, Action Run)[] Tests = [
        ("native game hide waits for finalization then restores the requested window", ZoneHide),
        ("native loading and hidden game UI never reopen windows early", Loading),
        ("native user-close preference stays closed across zone transitions", ManualClose),
        ("native direct Hide dismissal closes only that window and never gets restored", DirectHide),
        ("native Hide2 dismissal is not mistaken for a recoverable loading hide", DirectSoftHide),
        ("native adapter recognizes toolkit-bypassing close and keeps the other window open", AdapterClose),
        ("native adapter distinguishes zoning model close and disposal from user dismissal", AdapterModel),
        ("native close lock blocks Escape, direct hide and soft dismissal until unlocked", CloseLock),
        ("native soft-hidden allocated windows use Show instead of a no-op Open", SoftHide),
        ("native close and reopen animations cannot repeatedly close or allocate", Animations),
        ("native form body scrolling and fixed start footer stay within the same window", FormBounds),
    ];
    private static void Check(bool ok) { if (!ok) throw new Exception("Native window lifetime/layout contract failed"); }
    private static void ZoneHide()
    {
        var state = new NativeWindowLifetime(); state.Setup(); state.Hidden();
        Check(state.Evaluate(true, true, true, false) == NativeWindowAction.None);
        state.Finalized(); Check(state.Evaluate(true, true, true, false) == NativeWindowAction.None);
        Check(state.Evaluate(true, true, false, false) == NativeWindowAction.Open);
        state.Setup(); Check(state.Evaluate(true, true, true, true) == NativeWindowAction.None);
    }
    private static void Loading()
    {
        var state = new NativeWindowLifetime();
        Check(state.Evaluate(true, false, false, false) == NativeWindowAction.None);
        state.Setup(); Check(state.Evaluate(true, false, true, true) == NativeWindowAction.Close);
        state.BeginClose(); state.Hidden(); state.Finalized();
        Check(state.Evaluate(true, false, false, false) == NativeWindowAction.None);
        Check(state.Evaluate(true, true, false, false) == NativeWindowAction.Open);
    }
    private static void ManualClose()
    {
        var state = new NativeWindowLifetime(); state.Setup();
        Check(state.BeginClose()); Check(!state.BeginClose());
        state.Hidden(); state.Finalized();
        Check(state.Evaluate(false, true, false, false) == NativeWindowAction.None);
        Check(state.Evaluate(false, false, false, false) == NativeWindowAction.None);
        Check(state.Evaluate(true, true, false, false) == NativeWindowAction.Open);
    }
    private static void SoftHide()
    {
        var state = new NativeWindowLifetime(); state.Setup();
        Check(state.Evaluate(true, true, true, false) == NativeWindowAction.Show);
        Check(state.Evaluate(false, true, true, false) == NativeWindowAction.Close);
    }
    private static void DirectHide()
    {
        var main = new NativeWindowLifetime(); main.Setup();
        var finder = new NativeWindowLifetime(); finder.Setup();
        Check(finder.Hidden(true)); Check(!finder.Hidden(true));
        finder.Finalized();
        Check(finder.Evaluate(false, true, false, false) == NativeWindowAction.None);
        Check(main.Evaluate(true, true, true, true) == NativeWindowAction.None);
        main.BeginClose(); Check(!main.Hidden(true)); // model close must not emit another dismissal
        var zone = new NativeWindowLifetime(); zone.Setup(); Check(!zone.Hidden(false));
        zone.Finalized(); Check(zone.Evaluate(true, true, false, false) == NativeWindowAction.Open);
    }
    private static void DirectSoftHide()
    {
        var state = new NativeWindowLifetime(); state.Setup();
        Check(!state.DismissedWhileReady(true, false)); // setup/open animation
        Check(!state.DismissedWhileReady(true, true));
        Check(state.DismissedWhileReady(true, false));
        Check(!state.DismissedWhileReady(true, false));
        state.Finalized(); Check(state.Evaluate(false, true, false, false) == NativeWindowAction.None);
        state.Setup(); state.DismissedWhileReady(true, true);
        Check(!state.DismissedWhileReady(false, false));
        Check(!state.DismissedWhileReady(true, false)); // hidden by game UI/loading
        Check(state.Evaluate(true, true, true, false) == NativeWindowAction.Show);
    }
    private static void Animations()
    {
        var state = new NativeWindowLifetime(); state.Setup(); state.BeginClose();
        Check(state.Evaluate(true, true, true, false) == NativeWindowAction.None);
        Check(state.Evaluate(false, true, true, true) == NativeWindowAction.None);
        state.Finalized(); Check(state.Evaluate(true, true, true, false) == NativeWindowAction.None);
        Check(state.Evaluate(true, true, false, false) == NativeWindowAction.Open);
    }
    private static void FormBounds()
    {
        foreach (var size in new[] { new Vector2(380, 380), new(600, 540), new(640, 560), new(900, 700) })
        foreach (var start in new[] { new Vector2(14, 42), new(20, 52) })
        foreach (var rows in new[] { 0, 1, 2 })
        {
            var bounds = NativeFormLayout.Arrange(size, start, rows);
            Check(start.X + bounds.Body.X <= size.X && start.Y + bounds.Body.Y <= size.Y);
            Check(bounds.ScrollTop >= 0 && bounds.ScrollHeight >= 40);
            Check(bounds.ScrollTop + bounds.ScrollHeight < bounds.FooterY);
            Check(bounds.FooterY + 36 <= bounds.Body.Y);
        }
    }
    private sealed class Probe(Action closed, Func<bool> ready) : NativePersistentAddon(closed, ready)
    {
        internal void ToggleLock() => ToggleCloseLock();
    }

    private static void CloseLock()
    {
        var closes = 0;
        using var target = new Probe(() => closes++, () => true);
        target.SyncVisibility(true, true);
        target.SyncVisibility(true, true);
        target.ToggleLock();
        target.Close();
        Check(target.IsOpen && closes == 0);
        target.NativeSoftHide();
        target.SyncVisibility(true, true);
        Check(target.IsOpen && closes == 0);
        target.NativeHide();
        target.SyncVisibility(true, true);
        Check(target.IsOpen && target.CloseLocked && closes == 0);
        target.SyncVisibility(false, true);
        Check(!target.IsOpen && closes == 0);
        target.SyncVisibility(true, true);
        target.ToggleLock();
        target.Close();
        Check(!target.IsOpen && closes == 1);
    }
    private static void AdapterClose()
    {
        var ready = true; var closes = 0; var otherCloses = 0;
        using var target = new Probe(() => closes++, () => ready);
        using var main = new Probe(() => otherCloses++, () => ready);
        target.SyncVisibility(true, ready); main.SyncVisibility(true, ready);
        target.NativeHide(); Check(closes == 1 && main.IsOpen && otherCloses == 0);
        for (var i = 0; i < 10; i++) target.SyncVisibility(false, ready);
        Check(!target.IsOpen && closes == 1);
        target.SyncVisibility(true, ready); target.SyncVisibility(true, ready);
        target.NativeSoftHide(); target.SyncVisibility(true, ready); // stale published snapshot
        Check(closes == 2 && !target.IsOpen && main.IsOpen);
        target.SyncVisibility(false, ready); Check(!target.IsOpen);
        target.SyncVisibility(true, ready); target.Close(); Check(closes == 3 && !target.IsOpen);
    }
    private static void AdapterModel()
    {
        var ready = true; var closes = 0;
        using var target = new Probe(() => closes++, () => ready);
        target.SyncVisibility(true, ready); ready = false; target.NativeHide();
        target.SyncVisibility(true, ready); Check(!target.IsOpen && closes == 0);
        ready = true; target.SyncVisibility(true, ready); Check(target.IsOpen);
        ready = false; target.Close(); Check(closes == 0);
        ready = true; target.SyncVisibility(true, ready); Check(target.IsOpen);
        target.SyncVisibility(false, ready); Check(!target.IsOpen && closes == 0);
        target.SyncVisibility(true, ready); target.Dispose(); Check(closes == 0);
        target.SyncVisibility(true, ready); Check(!target.IsOpen);
    }
}
