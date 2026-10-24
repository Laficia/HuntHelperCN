using Dalamud.Bindings.ImGui;
using HuntHelper.Utilities;
using System.Collections.Generic;
using System.Numerics;

namespace HuntHelper.Gui;

internal readonly record struct TrainVersionTabBounds(int? Version, string Label, Vector2 Min, Vector2 Max);
public readonly record struct TrainVersionTabAction(bool Changed, int? Version);

public sealed class TrainVersionTabBar
{
    private readonly List<TrainVersionTabBounds> _bounds = new();
    internal IReadOnlyList<TrainVersionTabBounds> VisibleTabs => _bounds;

    public TrainVersionTabAction Draw(IReadOnlyList<TrainVersionGroup> groups, int? selected, float scale = 1)
    {
        _bounds.Clear();
        var result = new TrainVersionTabAction(false, selected);
        if (groups.Count == 0) return result;
        var height = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize + 4 * scale;
        if (ImGui.BeginChild("TrainVersionTabs", new Vector2(0, height), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (var group in groups)
            {
                if (_bounds.Count > 0) ImGui.SameLine();
                ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(group.Version == selected ? ImGuiCol.ButtonActive : ImGuiCol.Button));
                if (ImGui.Button($"{group.Label} ({group.Count})##Version{group.Version?.ToString() ?? "All"}"))
                    result = new TrainVersionTabAction(true, group.Version);
                ImGui.PopStyleColor();
                _bounds.Add(new(group.Version, group.Label, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
            }
        }
        ImGui.EndChild();
        return result;
    }
}
