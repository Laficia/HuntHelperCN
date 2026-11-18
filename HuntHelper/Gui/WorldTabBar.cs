using Dalamud.Bindings.ImGui;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

public readonly record struct WorldTabAction(uint? Selected, uint? Deleted, bool OrderChanged);
internal readonly record struct WorldTabBounds(uint WorldId, Vector2 Min, Vector2 Max, bool Hovered);

/// <summary>A server tab strip whose visible order is always the saved order.</summary>
public sealed class WorldTabBar
{
    private readonly TabDragGesture _drag = new();
    private readonly List<WorldTabBounds> _bounds = new();
    internal IReadOnlyList<WorldTabBounds> VisibleTabs => _bounds;

    public WorldTabAction Draw(IReadOnlyList<HuntTrainWorld> worlds, uint selected, List<uint> order, float scale = 1)
    {
        uint? selection = null;
        uint? deletion = null;
        var changed = WorldTabOrder.Synchronize(order, worlds.Select(w => w.WorldID));
        var labels = worlds.ToDictionary(w => w.WorldID, w => $"{w.WorldName} ({w.Count})");
        _bounds.Clear();
        if (worlds.Count == 0) { _drag.EndFrame(); return new(null, null, changed); }

        // Buttons avoid the native tab bar's cached display order. No drag/drop payload
        // is used, so ImGui cannot add its yellow drop target or floating preview.
        if (TrainTabStrip.Begin("HuntTrainServerTabs", labels.Values, scale, out var height))
        {
            var accent = new TrainTheme(TrainButtonTone.Accent);
            var neutral = new TrainTheme(TrainButtonTone.Neutral);
            foreach (var worldId in order.ToArray())
            {
                var label = labels[worldId];
                if (_bounds.Count != 0) ImGui.SameLine();
                var theme = worldId == selected ? accent : neutral;
                var clicked = theme.Button($"{label}###Server{worldId}", new(TrainTabStrip.ButtonWidth(label), height));
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                _bounds.Add(new(worldId, min, max, ImGui.IsMouseHoveringRect(min, max)));
                _drag.RegisterItem(worldId);
                if (clicked && !_drag.Dragging) selection = worldId;

                if (ImGui.BeginPopupContextItem($"ServerMenu{worldId}"))
                {
                    var index = order.IndexOf(worldId);
                    if (ImGui.MenuItem("选择")) selection = worldId;
                    if (ImGui.MenuItem("向左移动", enabled: index > 0)) changed |= WorldTabOrder.MoveBy(order, worldId, -1);
                    if (ImGui.MenuItem("向右移动", enabled: index + 1 < order.Count)) changed |= WorldTabOrder.MoveBy(order, worldId, 1);
                    ImGui.Separator();
                    if (ImGui.MenuItem("删除该服记录及标签")) deletion = worldId;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("删除当前所在服务器时会暂停录制；重新开始录制可再次扫描附近 A 怪。");
                    ImGui.EndPopup();
                }
            }

            changed |= _drag.Finish(order, _bounds.Select(b => (b.WorldId, b.Min, b.Max)), scale);
        }
        TrainTabStrip.End();
        _drag.EndFrame();
        return new(selection, deletion, changed);
    }
}
