namespace HuntHelper.Gui.Native;

internal enum NativeWindowAction { None, Open, Show, Close }

/// <summary>Keeps native allocation state separate from the user's requested visibility.</summary>
internal sealed class NativeWindowLifetime
{
    private bool setup, closing, seenVisible;
    internal void Setup() { setup = true; closing = seenVisible = false; }
    internal bool BeginClose()
    {
        if (closing) return false;
        closing = true; return true;
    }
    // Some native close buttons call Hide directly, bypassing NativeAddon.Close.
    // Only an unrequested hide while the game is interactive is a user dismissal.
    internal bool Hidden(bool interactive = false)
    {
        var userDismissed = setup && !closing && interactive;
        closing = true;
        return userDismissed;
    }
    internal void Finalized() { setup = closing = seenVisible = false; }
    internal bool DismissedWhileReady(bool ready, bool visible)
    {
        if (!ready) { seenVisible = false; return false; }
        if (visible) { seenVisible = true; return false; }
        // Hide2 bypasses OnHide in the bundled toolkit. After a visible frame,
        // hiding while the game remains usable is also a dismissal, not zoning.
        return seenVisible && setup && !closing && BeginClose();
    }

    internal NativeWindowAction Evaluate(bool wanted, bool ready, bool allocated, bool visible)
    {
        if (!wanted || !ready) return allocated && setup && !closing ? NativeWindowAction.Close : NativeWindowAction.None;
        if (closing || visible) return NativeWindowAction.None;
        if (!allocated) return NativeWindowAction.Open;
        return setup ? NativeWindowAction.Show : NativeWindowAction.None;
    }
}
