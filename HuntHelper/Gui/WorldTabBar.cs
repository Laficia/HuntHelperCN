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
        var byId = worlds.ToDictionary(w => w.WorldID);
        _bounds.Clear();
        if (worlds.Count == 0) { _drag.EndFrame(); return new(null, null, changed); }

        // Buttons avoid the native tab bar's cached display order. No drag/drop payload
        // is used, so ImGui cannot add its yellow drop target or floating preview.
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
        var height = ImGui.GetFrameHeight() + ImGui.GetStyle().ScrollbarSize + 4 * scale;
        if (ImGui.BeginChild("HuntTrainServerTabs", new Vector2(0, height), false, ImGuiWindowFlags.HorizontalScrollbar))
        {
            foreach (var worldId in order.ToArray())
            {
                var world = byId[worldId];
                if (_bounds.Count != 0) ImGui.SameLine();
                var color = ImGui.GetColorU32(worldId == selected ? ImGuiCol.ButtonActive : ImGuiCol.Button);
                ImGui.PushStyleColor(ImGuiCol.Button, color);
                var clicked = ImGui.Button($"{world.WorldName} ({world.Count})###Server{worldId}");
                ImGui.PopStyleColor();
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
        ImGui.EndChild();
        ImGui.PopStyleVar();
        _drag.EndFrame();
        return new(selection, deletion, changed);
    }
}
