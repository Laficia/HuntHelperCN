using Dalamud.Bindings.ImGui;

namespace HuntHelper.Gui;

public enum HuntFinderAction { None, Start, Stop }

public static class HuntFinderActionButton
{
    public static HuntFinderAction Draw(bool active, bool recording)
    {
        ImGui.BeginDisabled(recording && !active);
        var clicked = ImGui.Button(active ? "停止找怪###FinderStartStop" : "开始找怪###FinderStartStop");
        ImGui.EndDisabled();
        return clicked ? active ? HuntFinderAction.Stop : HuntFinderAction.Start : HuntFinderAction.None;
    }
}
