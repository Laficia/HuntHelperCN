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
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(11, 5) * scale);
        var height = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize + 4 * scale;
        if (ImGui.BeginChild("TrainVersionTabs", new Vector2(0, height), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (var group in groups)
            {
                if (_bounds.Count > 0) ImGui.SameLine();
                var active = group.Version == selected;
                ImGui.PushStyleColor(ImGuiCol.Button, active ? new Vector4(.17f, .38f, .58f, 1) : new Vector4(.11f, .15f, .21f, 1));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.22f, .43f, .63f, 1));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.2f, .47f, .7f, 1));
                ImGui.PushStyleColor(ImGuiCol.Text, active ? new Vector4(.95f, .98f, 1, 1) : new Vector4(.67f, .74f, .83f, 1));
                if (ImGui.Button($"{group.DisplayLabel}##Version{group.Version?.ToString() ?? "All"}"))
                    result = new TrainVersionTabAction(true, group.Version);
                ImGui.PopStyleColor(4);
                _bounds.Add(new(group.Version, group.Label, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
            }
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        return result;
    }
}
