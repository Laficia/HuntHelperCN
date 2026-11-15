namespace HuntHelper.Gui.Native;

internal enum NativeWindowAction { None, Open, Show, Close }

/// <summary>Keeps native allocation state separate from the user's requested visibility.</summary>
internal sealed class NativeWindowLifetime
{
    private bool setup, closing;
    internal void Setup() { setup = true; closing = false; }
    internal bool BeginClose()
    {
        if (closing) return false;
        closing = true; return true;
    }
    internal void Hidden() => closing = true;
    internal void Finalized() { setup = closing = false; }

    internal NativeWindowAction Evaluate(bool wanted, bool ready, bool allocated, bool visible)
    {
        if (!wanted || !ready) return allocated && setup && !closing ? NativeWindowAction.Close : NativeWindowAction.None;
        if (closing || visible) return NativeWindowAction.None;
        if (!allocated) return NativeWindowAction.Open;
        return setup ? NativeWindowAction.Show : NativeWindowAction.None;
    }
}
