using Dalamud.Bindings.ImGui;
using HuntHelper.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

internal readonly record struct TrainVersionTabBounds(int? Version, string Label, Vector2 Min, Vector2 Max);
public readonly record struct TrainVersionTabAction(bool Changed, int? Version, bool OrderChanged = false);

public sealed class TrainVersionTabBar
{
    private readonly List<TrainVersionTabBounds> _bounds = new();
    private readonly List<uint> _defaultOrder = new();
    private readonly TabDragGesture _drag = new();
    internal IReadOnlyList<TrainVersionTabBounds> VisibleTabs => _bounds;

    public TrainVersionTabAction Draw(IReadOnlyList<TrainVersionGroup> groups, int? selected, float scale = 1, List<uint>? order = null)
    {
        _bounds.Clear();
        var result = new TrainVersionTabAction(false, selected);
        order ??= _defaultOrder;
        static uint Key(int? version) => version.HasValue ? (uint)version.Value : uint.MaxValue;
        var byId = groups.ToDictionary(g => Key(g.Version));
        var orderChanged = WorldTabOrder.Synchronize(order, groups.Select(g => Key(g.Version)));
        if (groups.Count == 0) { _drag.EndFrame(); return result with { OrderChanged = orderChanged }; }
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 6 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(11, 5) * scale);
        var height = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize + 4 * scale;
        if (ImGui.BeginChild("TrainVersionTabs", new Vector2(0, height), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (var id in order.ToArray())
            {
                var group = byId[id];
                if (_bounds.Count > 0) ImGui.SameLine();
                var active = group.Version == selected;
                ImGui.PushStyleColor(ImGuiCol.Button, active ? new Vector4(.17f, .38f, .58f, 1) : new Vector4(.11f, .15f, .21f, 1));
                ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.22f, .43f, .63f, 1));
                ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.2f, .47f, .7f, 1));
                ImGui.PushStyleColor(ImGuiCol.Text, active ? new Vector4(.95f, .98f, 1, 1) : new Vector4(.67f, .74f, .83f, 1));
                var clicked = ImGui.Button($"{group.DisplayLabel}###Version{group.Version?.ToString() ?? "All"}");
                _drag.RegisterItem(id);
                if (clicked && !_drag.Dragging)
                    result = new TrainVersionTabAction(true, group.Version);
                ImGui.PopStyleColor(4);
                _bounds.Add(new(group.Version, group.Label, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
                if (ImGui.BeginPopupContextItem($"VersionMenu{id}"))
                {
                    var index = order.IndexOf(id);
                    if (ImGui.MenuItem("选择")) result = new(true, group.Version);
                    if (ImGui.MenuItem("向左移动", enabled: index > 0)) orderChanged |= WorldTabOrder.MoveBy(order, id, -1);
                    if (ImGui.MenuItem("向右移动", enabled: index + 1 < order.Count)) orderChanged |= WorldTabOrder.MoveBy(order, id, 1);
                    ImGui.EndPopup();
                }
            }
            orderChanged |= _drag.Finish(order, _bounds.Select(b => (Key(b.Version), b.Min, b.Max)), scale);
        }
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        _drag.EndFrame();
        return result with { OrderChanged = orderChanged };
    }
}
