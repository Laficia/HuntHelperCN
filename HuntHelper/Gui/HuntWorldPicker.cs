using Dalamud.Bindings.ImGui;
using HuntHelper.Managers.Hunts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

/// <summary>Shared production/test view; selections append in click order across DC filters.</summary>
internal sealed class HuntWorldPicker
{
    private string? _dataCenter;
    internal string DataCenter => _dataCenter ?? string.Empty;
    internal (Vector2 Min, Vector2 Max) ComboBounds { get; private set; }
    internal readonly Dictionary<string,(Vector2 Min,Vector2 Max)> GroupBounds = new();
    internal readonly Dictionary<uint,(Vector2 Min,Vector2 Max)> WorldBounds = new();
    internal IReadOnlyList<HuntTrainWorld> VisibleWorlds { get; private set; } = Array.Empty<HuntTrainWorld>();

    public bool Draw(IReadOnlyList<HuntTrainWorld> worlds, uint current, List<uint> order, float scale = 1)
    {
        var changed = false;
        var groups = worlds.Select(w => w.DataCenterName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
        _dataCenter ??= worlds.FirstOrDefault(w => w.WorldID == current)?.DataCenterName ?? string.Empty;
        if (_dataCenter.Length > 0 && !groups.Contains(_dataCenter)) _dataCenter = string.Empty;
        ImGui.TextUnformatted("大区"); ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Min(ImGui.GetContentRegionAvail().X, 280 * scale));
        GroupBounds.Clear(); WorldBounds.Clear();
        var preview = _dataCenter.Length == 0 ? $"全部大区（{worlds.Count}服）" : $"{_dataCenter}（{worlds.Count(w => w.DataCenterName == _dataCenter)}服）";
        if (ImGui.BeginCombo("##FinderDataCenter", preview))
        {
            if (ImGui.Selectable($"全部大区（{worlds.Count}服）", _dataCenter.Length == 0)) _dataCenter = string.Empty;
            GroupBounds[""] = (ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            foreach (var group in groups)
            {
                if (ImGui.Selectable($"{group}（{worlds.Count(w => w.DataCenterName == group)}服）", _dataCenter == group)) _dataCenter = group;
                GroupBounds[group] = (ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
            }
            ImGui.EndCombo();
        }
        ComboBounds = (ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
        VisibleWorlds = worlds.Where(w => _dataCenter.Length == 0 || w.DataCenterName == _dataCenter).ToList();
        ImGui.TextDisabled($"已加载 {groups.Count} 个大区 · {worlds.Count} 个服务器");
        if (VisibleWorlds.Count == 0) ImGui.TextWrapped("服务器名单暂未加载，请稍后刷新");
        else
        {
            var columns = ImGui.GetContentRegionAvail().X >= 380 * scale ? 2 : 1;
            var rows = (VisibleWorlds.Count + columns - 1) / columns;
            var height = Math.Clamp(rows * ImGui.GetFrameHeightWithSpacing() + 12 * scale, 92 * scale, 190 * scale);
            if (ImGui.BeginChild("FinderWorlds",new(0,height),true))
            {
                if (ImGui.BeginTable("WorldChoices",columns,ImGuiTableFlags.SizingStretchSame))
                {
                    foreach (var world in VisibleWorlds)
                    {
                        ImGui.TableNextColumn();
                        var index = order.IndexOf(world.WorldID); var selected = index >= 0;
                        if (ImGui.Checkbox($"{(selected ? (index + 1) + ". " : "")}{world.WorldName}###FinderWorld{world.WorldID}",ref selected))
                        { if (selected) order.Add(world.WorldID); else order.Remove(world.WorldID); changed = true; }
                        WorldBounds[world.WorldID] = (ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
                    }
                    ImGui.EndTable();
                }
            }
            ImGui.EndChild();
        }
        ImGui.Spacing(); ImGui.TextUnformatted($"执行顺序（{order.Count}服）");
        var names = order.Select((id,i) => $"{i+1}. {worlds.FirstOrDefault(w => w.WorldID == id)?.WorldName ?? $"区服{id}"}");
        ImGui.TextWrapped(order.Count == 0 ? "请按执行先后勾选服务器" : string.Join("  →  ",names));
        if (ImGui.SmallButton("清空服务器选择")) { order.Clear(); changed = true; }
        return changed;
    }
}
