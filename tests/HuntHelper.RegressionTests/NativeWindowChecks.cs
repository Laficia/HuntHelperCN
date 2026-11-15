using HuntHelper.Gui.Native;
using System.Numerics;

internal static class NativeWindowChecks
{
    internal static readonly (string Name, Action Run)[] Tests = [
        ("native game hide waits for finalization then restores the requested window", ZoneHide),
        ("native loading and hidden game UI never reopen windows early", Loading),
        ("native user-close preference stays closed across zone transitions", ManualClose),
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
}
