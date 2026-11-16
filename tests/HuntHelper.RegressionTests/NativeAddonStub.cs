// Minimal toolkit boundary for exercising the real NativePersistentAddon adapter.
// No FFXIV/KamiToolKit assemblies or game memory are loaded by these checks.
using System.Runtime.InteropServices;

namespace FFXIVClientStructs.FFXIV.Component.GUI
{
    public struct AtkValue { }
    public struct AtkUnitBase
    {
        internal bool Visible;
        public void Show(bool silence, uint flags) { Visible = true; }
    }
}

namespace KamiToolKit.BaseTypes
{
    using FFXIVClientStructs.FFXIV.Component.GUI;
    public unsafe class NativeAddon : IDisposable
    {
        protected AtkUnitBase* InternalAddon;
        public bool IsOpen => InternalAddon != null && InternalAddon->Visible;
        public void Open()
        {
            if (InternalAddon != null) return;
            InternalAddon = (AtkUnitBase*)NativeMemory.AllocZeroed((nuint)sizeof(AtkUnitBase));
            OnSetup(InternalAddon, []); InternalAddon->Visible = true;
        }
        public virtual void Close()
        {
            if (InternalAddon == null) return;
            InternalAddon->Visible = false; OnHide(InternalAddon); FinalizeStub();
        }
        internal void NativeHide()
        {
            if (InternalAddon == null) return;
            InternalAddon->Visible = false; OnHide(InternalAddon); FinalizeStub();
        }
        internal void NativeSoftHide() { if (InternalAddon != null) InternalAddon->Visible = false; }
        private void FinalizeStub()
        {
            OnFinalize(InternalAddon); NativeMemory.Free(InternalAddon); InternalAddon = null;
        }
        protected virtual void OnSetup(AtkUnitBase* addon, Span<AtkValue> values) { }
        protected virtual void OnHide(AtkUnitBase* addon) { }
        protected virtual void OnFinalize(AtkUnitBase* addon) { }
        public virtual void Dispose() { if (InternalAddon != null) { OnFinalize(InternalAddon); NativeMemory.Free(InternalAddon); InternalAddon = null; } }
    }
}
