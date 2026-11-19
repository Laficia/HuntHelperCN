using System;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;

namespace HuntHelper.Gui.Native;

/// <summary>Game hides do not overwrite the user's open/closed preference.</summary>
internal abstract partial class NativePersistentAddon(Action userClose, Func<bool> canInteract) : NativeAddon
{
    private readonly NativeWindowLifetime lifetime = new();
    private bool disposing;
    internal bool CloseLocked { get; private set; }
    protected void ToggleCloseLock() => CloseLocked = !CloseLocked;

    partial void ArrangeCloseLock();
    partial void ClearCloseLockButton();

    public override unsafe void Close()
    {
        // KamiToolKit routes the title close button and Escape through this virtual method.
        // Native zone-change Hide calls bypass it and must never become a user close.
        if (InternalAddon == null || CloseLocked) return;
        if (lifetime.BeginClose() && !disposing && canInteract()) userClose();
        base.Close();
    }

    internal unsafe void CloseFromModel()
    {
        if (InternalAddon == null) return;
        lifetime.BeginClose(); base.Close();
    }

    internal unsafe void SyncVisibility(bool wanted, bool ready)
    {
        if (disposing) return;
        if (!CloseLocked && lifetime.DismissedWhileReady(ready, IsOpen)) { userClose(); base.Close(); return; }
        switch (lifetime.Evaluate(wanted, ready, InternalAddon != null, IsOpen))
        {
            case NativeWindowAction.Close: CloseFromModel(); break;
            case NativeWindowAction.Open: Open(); break;
            // Hide2 can leave a native addon allocated. Open() alone does not show it.
            case NativeWindowAction.Show: InternalAddon->Show(false, 0); break;
        }
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    { lifetime.Setup(); base.OnSetup(addon, values); }
    protected override unsafe void OnHide(AtkUnitBase* addon)
    {
        if (lifetime.Hidden(!CloseLocked && !disposing && canInteract())) userClose();
        base.OnHide(addon);
    }
    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    { ClearCloseLockButton(); lifetime.Finalized(); base.OnFinalize(addon); }

    protected void UpdateCloseLockButton() => ArrangeCloseLock();

    public override void Dispose()
    { disposing = true; CloseFromModel(); base.Dispose(); }
}
