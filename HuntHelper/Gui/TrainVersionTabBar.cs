using Dalamud.Bindings.ImGui;
using HuntHelper.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

internal readonly record struct TrainVersionTabBounds(int? Version, string Label, Vector2 Min, Vector2 Max);
public readonly record struct TrainVersionTabAction(bool Changed, int? Version, bool OrderChanged = false, int? ClearVersion = null);

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
        if (TrainTabStrip.Begin("TrainVersionTabs", groups.Select(g => g.DisplayLabel), scale, out var height))
        {
            var accent = new TrainTheme(TrainButtonTone.Accent);
            var neutral = new TrainTheme(TrainButtonTone.Neutral);
            foreach (var id in order.ToArray())
            {
                var group = byId[id];
                var label = group.DisplayLabel;
                if (_bounds.Count > 0) ImGui.SameLine();
                var theme = group.Version == selected ? accent : neutral;
                var clicked = theme.Button($"{label}###Version{group.Version?.ToString() ?? "All"}", new(TrainTabStrip.ButtonWidth(label), height));
                _drag.RegisterItem(id);
                if (clicked && !_drag.Dragging)
                    result = new TrainVersionTabAction(true, group.Version);
                _bounds.Add(new(group.Version, group.Label, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
                if (ImGui.BeginPopupContextItem($"VersionMenu{id}"))
                {
                    var index = order.IndexOf(id);
                    if (ImGui.MenuItem("选择")) result = new(true, group.Version);
                    if (ImGui.MenuItem("向左移动", enabled: index > 0)) orderChanged |= WorldTabOrder.MoveBy(order, id, -1);
                    if (ImGui.MenuItem("向右移动", enabled: index + 1 < order.Count)) orderChanged |= WorldTabOrder.MoveBy(order, id, 1);
                    if (group.Version.HasValue)
                    {
                        ImGui.Separator();
                        if (ImGui.MenuItem("清空此标签", enabled: group.Count > 0)) result = result with { ClearVersion = group.Version };
                    }
                    ImGui.EndPopup();
                }
            }
            orderChanged |= _drag.Finish(order, _bounds.Select(b => (Key(b.Version), b.Min, b.Max)), scale);
        }
        TrainTabStrip.End();
        _drag.EndFrame();
        return result with { OrderChanged = orderChanged };
    }
}
