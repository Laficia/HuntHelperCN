using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using HuntHelper.Managers.Automation;
using System;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

public sealed partial class HuntFinderUI
{
    private bool _compactOptions;

    public void Draw()
    {
        if (!_open || NativePresentation) return;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(600, 540) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(360, 300) * scale, new Vector2(float.MaxValue));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 6) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.055f, .075f, .105f, .98f));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(.10f, .15f, .21f, 1));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.10f, .15f, .21f, 1));
        if (ImGui.Begin("A 怪找怪##HuntFinder", ref _open, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var start = false;
            if (ImGui.Button(_automation.Session.Active ? "停止找怪" : "开始找怪", new Vector2(96, 28) * scale))
            { if (_automation.Session.Active) _automation.Session.Stop(); else start = true; }
            ImGui.SameLine(); if (ImGui.Button("设置", new Vector2(64, 28) * scale)) _compactOptions = !_compactOptions;
            if (OpenNativeUi != null) { ImGui.SameLine(); if (ImGui.Button("原生窗口", new Vector2(88, 28) * scale)) OpenNativeUi(); }
            ImGui.Separator();
            if (ImGui.BeginChild("FinderSettings", new Vector2(0, -30 * scale), false)) DrawCompactFinder(scale);
            ImGui.EndChild();
            if (start) StartPatrol();
            var status = _feedback.Length > 0 ? _feedback : _automation.Session.Message;
            if (_automation.Session.Active) status += $" · 已发现 A {_automation.Session.FoundA}";
            ImGui.TextUnformatted(status);
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(status);
        }
        ImGui.End(); ImGui.PopStyleColor(3); ImGui.PopStyleVar(3);
    }

    private void DrawCompactFinder(float scale)
    {
        ImGui.BeginDisabled(_automation.Session.Active);
        ImGui.SetNextItemWidth(Math.Max(120 * scale, ImGui.GetContentRegionAvail().X - 90 * scale));
        if (ImGui.BeginCombo("寻路模式", _options.RouteMode == PatrolRouteMode.DRCoverage ? "DR 巡航" : "预设点位"))
        {
            if (ImGui.Selectable("DR 巡航", _options.RouteMode == PatrolRouteMode.DRCoverage)) SetRouteMode(PatrolRouteMode.DRCoverage);
            if (ImGui.Selectable("预设点位", _options.RouteMode == PatrolRouteMode.Preset)) SetRouteMode(PatrolRouteMode.Preset);
            ImGui.EndCombo();
        }
        var dr = _options.RouteMode == PatrolRouteMode.DRCoverage;
        var maps = _automation.GetMaps(_options.RouteMode);
        DrawWorldSelection();
        ImGui.SetNextItemWidth(Math.Max(120 * scale, ImGui.GetContentRegionAvail().X - 90 * scale));
        if (ImGui.BeginCombo("版本", _expansion < 0 ? "全部版本" : $"{_expansion + 2}.0"))
        {
            if (ImGui.Selectable("全部版本", _expansion == -1)) _expansion = -1;
            foreach (var version in maps.Select(m => m.Expansion).Distinct().Order())
                if (ImGui.Selectable($"{version + 2}.0", _expansion == version)) _expansion = version;
            ImGui.EndCombo();
        }
        var visible = maps.Where(m => _expansion < 0 || m.Expansion == _expansion).ToList();
        if (ImGui.Button("全选本版本", new Vector2(100, 28) * scale)) foreach (var map in visible) _selectedMaps.Add(map.TerritoryId);
        ImGui.SameLine(); if (ImGui.Button("清空", new Vector2(60, 28) * scale)) _selectedMaps.Clear();
        ImGui.SameLine(); ImGui.TextDisabled($"已选 {_selectedMaps.Count} 张");
        var height = Math.Clamp(ImGui.GetContentRegionAvail().Y - 8 * scale, 120 * scale, 240 * scale);
        if (ImGui.BeginChild("FinderMaps", new Vector2(0, height), true))
        {
            var columns = ImGui.GetContentRegionAvail().X >= 440 * scale ? 2 : 1;
            if (ImGui.BeginTable("FinderMapGrid", columns))
            {
                foreach (var map in visible)
                {
                    ImGui.TableNextColumn(); var selected = _selectedMaps.Contains(map.TerritoryId);
                    if (ImGui.Checkbox($"{map.Expansion + 2}.0 · {map.Name}##Find{map.TerritoryId}", ref selected))
                    { if (selected) _selectedMaps.Add(map.TerritoryId); else _selectedMaps.Remove(map.TerritoryId); }
                }
                ImGui.EndTable();
            }
        }
        ImGui.EndChild();
        if (_compactOptions) DrawCompactOptions(dr, scale);
        ImGui.EndDisabled();
        if (_compactOptions)
        {
            var monitor = _config.HuntDeathMonitoring;
            if (ImGui.Checkbox("自动监测怪物死亡", ref monitor)) { _config.HuntDeathMonitoring = monitor; _config.Save(); }
        }
        if (dr && _automation.CoverageDataError.Length > 0) ImGui.TextWrapped(_automation.CoverageDataError);
    }

    private void DrawCompactOptions(bool dr, float scale)
    {
        ImGui.Separator();
        var recorded = _options.UseRecordedHunts;
        if (ImGui.Checkbox("跳过已找齐的地图", ref recorded))
        { _options.UseRecordedHunts = _config.HuntFinderUseRecordedHunts = recorded; _config.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("仅采用本服、本图、本线最近60分钟的现场存活记录；Sonar和导入记录不作为已扫依据。");
        if (dr)
        {
            ImGui.TextDisabled("自动飞行 · 当前分线");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("400米是DR算法假设，不是实测距离。使用正常水晶传送与vnavmesh飞行，不自动切线。");
            return;
        }
        var a = _options.RankA; var b = _options.RankB;
        ImGui.Checkbox("A 怪", ref a); ImGui.SameLine(); ImGui.Checkbox("B 怪", ref b); _options.RankA = a; _options.RankB = b;
        var mount = _options.Mount; var fly = _options.Takeoff;
        ImGui.Checkbox("自动上坐骑", ref mount); ImGui.SameLine(); ImGui.Checkbox("自动起飞", ref fly); _options.Mount = mount; _options.Takeoff = fly;
        var smart = _options.SmartTeleport;
        if (ImGui.Checkbox("智能水晶传送", ref smart)) { _options.SmartTeleport = _config.HuntFinderSmartTeleport = smart; _config.Save(); }
        var overhead = _options.TeleportSeconds; ImGui.SetNextItemWidth(100 * scale);
        if (ImGui.InputFloat("读条 / 加载（秒）", ref overhead, 0, 0, "%.0f"))
        { _options.TeleportSeconds = _config.HuntFinderTeleportSeconds = float.IsFinite(overhead) ? Math.Clamp(overhead, 10, 120) : 25; _config.Save(); }
        var adaptive = _options.AdaptiveDetection;
        if (ImGui.Checkbox("自适应探测距离", ref adaptive)) { _options.AdaptiveDetection = _config.HuntFinderAdaptiveDetection = adaptive; _config.Save(); }
        var radius = _options.Radius; var wait = _options.WaitMs / 1000;
        ImGui.SetNextItemWidth(100 * scale); ImGui.SliderFloat("停靠距离（米）", ref radius, 10, 150, "%.0f");
        ImGui.SetNextItemWidth(100 * scale); ImGui.SliderInt("点位等待（秒）", ref wait, 1, 30);
        _options.Radius = radius; _options.WaitMs = wait * 1000;
    }
}
