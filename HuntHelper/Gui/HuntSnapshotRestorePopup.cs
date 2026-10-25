using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace HuntHelper.Gui;

public static class HuntSnapshotRestorePopup
{
    public const string Title = "恢复记录备份##TrainSnapshotRestore";
    // Open at the window's root ID scope, even when requested from a settings tree.
    public static bool Begin(ref bool requested, float scale)
    {
        if (requested) { ImGui.OpenPopup(Title); requested = false; }
        if (!ImGui.IsPopupOpen(Title)) return false;
        ImGui.SetNextWindowSize(new Vector2(Math.Min(500 * scale,
            Math.Max(1, ImGui.GetMainViewport().WorkSize.X - 16)), 0), ImGuiCond.Appearing);
        return ImGui.BeginPopupModal(Title);
    }
}
