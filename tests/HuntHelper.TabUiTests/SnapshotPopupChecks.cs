using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using System.Numerics;

internal static class SnapshotPopupChecks
{
    public static void Run()
    {
        var request=false;
        ImGui.NewFrame();ImGui.SetNextWindowSize(new Vector2(700,300));
        ImGui.Begin("SnapshotPopupParent",ImGuiWindowFlags.NoSavedSettings);
        if(ImGui.TreeNodeEx("Settings",ImGuiTreeNodeFlags.DefaultOpen))
        {request=true;ImGui.TreePop();}
        if(!HuntSnapshotRestorePopup.Begin(ref request,1) || request)
            throw new Exception("Restore requested from settings did not open in the root ID scope");
        ImGui.TextUnformatted("Restore confirmation");
        ImGui.CloseCurrentPopup();ImGui.EndPopup();
        if(ImGui.IsPopupOpen(HuntSnapshotRestorePopup.Title))throw new Exception("Restore cancellation left a modal open");
        ImGui.End();ImGui.Render();
        Console.WriteLine("PASS Native restore confirmation opens from settings and closes without a stale popup");
    }
}
