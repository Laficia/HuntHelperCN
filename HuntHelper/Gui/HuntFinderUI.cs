using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using HuntHelper.Managers.Automation;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

public sealed class HuntFinderUI
{
    private readonly HuntAutomationManager _automation;
    private readonly Configuration _config;
    private readonly HashSet<uint> _selectedMaps = new();
    private readonly PatrolOptions _options = new();
    private bool _open, _custom = true;
    private int _expansion = -1;
    private string _feedback = string.Empty;

    public HuntFinderUI(HuntAutomationManager automation, Configuration config)
    { _automation = automation; _config = config; }
    public void Open()
    {
        _open = true;
        if (_selectedMaps.Count == 0 && _automation.Maps.Any(m => m.TerritoryId == _automation.Context.Territory))
            _selectedMaps.Add(_automation.Context.Territory);
    }

    public void Draw()
    {
        if (!_open) return;
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(610, 620) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(360, 300) * scale, new Vector2(float.MaxValue));
        if (ImGui.Begin("找怪 / 路线录制##HuntFinder", ref _open))
        {
            ImGui.TextWrapped(_automation.Session.Message);
            if (_automation.Session.Active)
            {
                ImGui.TextUnformatted($"本图已发现：A {_automation.Session.FoundA} / B {_automation.Session.FoundB}");
                if (ImGui.Button("停止找怪")) _automation.Session.Stop();
            }
            var monitor = _config.HuntDeathMonitoring;
            if (ImGui.Checkbox("自动监测怪物死亡", ref monitor)) { _config.HuntDeathMonitoring = monitor; _config.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("仅监测当前所在服务器、地图、分线；离开加载范围不会直接判死。手动恢复存活优先。");
            ImGui.Separator();
            if (ImGui.BeginTabBar("FinderTabs"))
            {
                if (ImGui.BeginTabItem("找怪")) { DrawFinder(); ImGui.EndTabItem(); }
                if (ImGui.BeginTabItem("路线录制")) { DrawRecorder(); ImGui.EndTabItem(); }
                ImGui.EndTabBar();
            }
            if (_automation.Store.Error != null) ImGui.TextWrapped($"路线文件：{_automation.Store.Error}");
            if (_feedback.Length > 0) ImGui.TextWrapped(_feedback);
        }
        ImGui.End();
    }

    private void DrawFinder()
    {
        ImGui.BeginDisabled(_automation.Session.Active || _automation.Recording);
        var rankA = _options.RankA; var rankB = _options.RankB;
        ImGui.Checkbox("A 怪", ref rankA); ImGui.SameLine(); ImGui.Checkbox("B 怪", ref rankB);
        _options.RankA = rankA; _options.RankB = rankB;
        var mount = _options.Mount; var fly = _options.Takeoff;
        ImGui.Checkbox("自动上坐骑", ref mount); ImGui.SameLine(); ImGui.Checkbox("可飞行时自动起飞", ref fly);
        _options.Mount = mount; _options.Takeoff = fly;
        ImGui.Checkbox("优先使用已录制路线（没有时使用内置点位）", ref _custom);
        var radius = _options.Radius; var wait = _options.WaitMs / 1000;
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("扫描距离（米）", ref radius, 10, 150, "%.0f");
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        ImGui.SliderInt("点位等待（秒）", ref wait, 1, 30);
        _options.Radius = radius; _options.WaitMs = wait * 1000;
        ImGui.Spacing();
        if (ImGui.BeginCombo("版本", _expansion < 0 ? "全部版本" : $"{_expansion + 2}.0"))
        {
            if (ImGui.Selectable("全部版本", _expansion == -1)) _expansion = -1;
            foreach (var version in _automation.Maps.Select(m => m.Expansion).Distinct().Order())
                if (ImGui.Selectable($"{version + 2}.0", _expansion == version)) _expansion = version;
            ImGui.EndCombo();
        }
        var visible = _automation.Maps.Where(m => _expansion < 0 || m.Expansion == _expansion).ToList();
        if (ImGui.Button("全选本版本地图")) foreach (var map in visible) _selectedMaps.Add(map.TerritoryId);
        ImGui.SameLine(); if (ImGui.Button("清空选择")) _selectedMaps.Clear();
        ImGui.TextUnformatted($"已选 {_selectedMaps.Count} 张地图（包括其他版本的选择）");
        if (ImGui.BeginChild("FinderMaps", new Vector2(0, 180 * ImGuiHelpers.GlobalScale), true))
        {
            foreach (var map in visible)
            {
                var selected = _selectedMaps.Contains(map.TerritoryId);
                var custom = _custom && _automation.Store.Routes.Any(r => r.TerritoryId == map.TerritoryId && r.Points.Count > 0);
                var route = _automation.GetRoute(map.TerritoryId, _custom);
                if (ImGui.Checkbox($"{map.Expansion + 2}.0 · {map.Name}（{(custom ? "录制" : "内置")} {route.Points.Count} 点）##Find{map.TerritoryId}", ref selected))
                { if (selected) _selectedMaps.Add(map.TerritoryId); else _selectedMaps.Remove(map.TerritoryId); }
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("开始找怪"))
        {
            var current = _automation.Context.Territory;
            var routes = _automation.Maps.Where(m => _selectedMaps.Contains(m.TerritoryId))
                .OrderByDescending(m => m.TerritoryId == current).ThenBy(m => m.Expansion).ThenBy(m => m.TerritoryId)
                .Select(m => _automation.GetRoute(m.TerritoryId, _custom));
            _automation.Session.Start(routes, _options, Environment.TickCount64);
        }
        ImGui.EndDisabled();
        ImGui.TextWrapped("扫描当前分线。跨地图使用已解锁水晶正常传送；找不到可用水晶时会停止提示。巡点只用 vnavmesh，不会直接 TP 到坐标。多层地图建议录制三维路线。");
    }

    private void DrawRecorder()
    {
        var current = _automation.Context;
        var map = _automation.Maps.FirstOrDefault(m => m.TerritoryId == current.Territory);
        ImGui.TextUnformatted(map == null ? "请进入可狩猎地图录制路线" : $"当前地图：{map.Name}");
        ImGui.BeginDisabled(_automation.Session.Active);
        ImGui.BeginDisabled(map == null);
        if (ImGui.Button(_automation.Recording ? "停止录制" : "开始录制")) _automation.ToggleRecording();
        ImGui.EndDisabled();
        ImGui.SameLine();
        var automatic = _automation.AutomaticRecording;
        ImGui.Checkbox("每移动 30 米自动录点", ref automatic); _automation.AutomaticRecording = automatic;
        ImGui.BeginDisabled(!_automation.Recording);
        if (ImGui.Button("录入当前点位")) _automation.AddPoint();
        ImGui.EndDisabled();
        ImGui.TextWrapped(_automation.RecorderMessage);
        var route = _automation.Store.Routes.FirstOrDefault(r => r.TerritoryId == current.Territory);
        if (route != null)
        {
            ImGui.BeginDisabled(_automation.Recording);
            if (ImGui.Button("按距离排序")) { PatrolRouteStore.Sort(route); _automation.Store.Save(); }
            ImGui.SameLine(); if (ImGui.Button("倒序")) { route.Points.Reverse(); _automation.Store.Save(); }
            ImGui.SameLine(); if (ImGui.Button("清空本图路线")) ImGui.OpenPopup("ClearPatrolRoute");
            if (ImGui.BeginPopup("ClearPatrolRoute"))
            {
                ImGui.TextUnformatted("清空这张地图的自定义点位？");
                if (ImGui.Button("确认清空")) { route.Points.Clear(); _automation.Store.Save(); ImGui.CloseCurrentPopup(); }
                ImGui.SameLine(); if (ImGui.Button("取消")) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
            }
            ImGui.EndDisabled();
            if (ImGui.BeginChild("RecordedPoints", new Vector2(0, 180 * ImGuiHelpers.GlobalScale), true))
            {
                for (var i = 0; i < route.Points.Count; i++)
                {
                    var p = route.Points[i];
                    ImGui.PushID(i);
                    ImGui.TextUnformatted($"{i + 1}：{p.Position?.ToString() ?? p.MapPosition.ToString()}");
                    ImGui.SameLine();
                    ImGui.BeginDisabled(_automation.Recording);
                    var changed = false;
                    if (i > 0 && ImGui.SmallButton("上移")) { (route.Points[i - 1], route.Points[i]) = (route.Points[i], route.Points[i - 1]); changed = true; }
                    ImGui.SameLine();
                    if (i + 1 < route.Points.Count && ImGui.SmallButton("下移")) { (route.Points[i + 1], route.Points[i]) = (route.Points[i], route.Points[i + 1]); changed = true; }
                    ImGui.SameLine(); if (ImGui.SmallButton("删除")) { route.Points.RemoveAt(i); changed = true; }
                    ImGui.EndDisabled(); ImGui.PopID();
                    if (changed) { _automation.Store.Save(); break; }
                }
            }
            ImGui.EndChild();
            DrawPreview(route);
        }
        ImGui.BeginDisabled(_automation.Recording);
        if (ImGui.Button("导出全部路线")) { ImGui.SetClipboardText(ExportImport.Export(_automation.Store.Routes)); _feedback = "HHT 巡点路线代码已复制"; }
        ImGui.SameLine(); if (ImGui.Button("覆盖导入路线")) ImGui.OpenPopup("ImportPatrolRoutes");
        if (ImGui.BeginPopup("ImportPatrolRoutes"))
        {
            ImGui.TextWrapped("将用剪贴板中的 HHT 巡点路线覆盖全部自定义路线。狩猎车记录不会被修改。");
            if (ImGui.Button("确认覆盖导入"))
            { _feedback = _automation.Store.Import(ImGui.GetClipboardText()) ? "路线导入成功" : "路线导入失败，已保留原路线"; ImGui.CloseCurrentPopup(); }
            ImGui.SameLine(); if (ImGui.Button("取消")) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
        ImGui.EndDisabled();
        ImGui.EndDisabled();
        ImGui.TextWrapped("路线几何按地图共享，找怪结果仍按服务器隔离。手动录点最少间隔 10 米，换图或换服会停止录制。");
    }

    private static void DrawPreview(PatrolRoute route)
    {
        var points = route.Points.Where(p => p.Position.HasValue).Select(p => new Vector2(p.Position!.Value.X, p.Position.Value.Z)).ToList();
        if (points.Count == 0) return;
        var size = new Vector2(Math.Min(300 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().X), 150 * ImGuiHelpers.GlobalScale);
        ImGui.InvisibleButton("RecordedRoutePreview", size);
        var start = ImGui.GetItemRectMin(); var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + size, 0xFF242424);
        var min = new Vector2(points.Min(p => p.X), points.Min(p => p.Y));
        var max = new Vector2(points.Max(p => p.X), points.Max(p => p.Y));
        var ratio = Math.Min((size.X - 20) / Math.Max(1, max.X - min.X), (size.Y - 20) / Math.Max(1, max.Y - min.Y));
        Vector2 Screen(Vector2 p) => start + new Vector2(10) + (p - min) * ratio;
        for (var i = 0; i < points.Count; i++)
        {
            if (i > 0) draw.AddLine(Screen(points[i - 1]), Screen(points[i]), 0xFF55BBDD, 2);
            draw.AddCircleFilled(Screen(points[i]), 3, i == 0 ? 0xFF55DD55 : 0xFF55BBDD);
        }
    }
}
