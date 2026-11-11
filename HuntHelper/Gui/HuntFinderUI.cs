using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using HuntHelper.Managers.Automation;
using HuntHelper.Utilities;
using HuntHelper.Managers.Hunts.Models;
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
    private bool _open;
    private int _expansion = -1;
    private string _feedback = string.Empty;
    private IReadOnlyList<HuntTrainWorld> _worldCatalog = Array.Empty<HuntTrainWorld>();
    private uint _catalogWorld;
    private readonly HuntWorldPicker _worldPicker = new();
    private long _nextCatalogRefresh;

    public HuntFinderUI(HuntAutomationManager automation, Configuration config)
    {
        _automation = automation; _config = config;
        _options.SmartTeleport = config.HuntFinderSmartTeleport;
        _options.TeleportSeconds = config.HuntFinderTeleportSeconds;
        _options.AdaptiveDetection = config.HuntFinderAdaptiveDetection;
        _options.UseRecordedHunts = config.HuntFinderUseRecordedHunts;
    }
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
        if (ImGui.Begin("A 怪找怪##HuntFinder", ref _open, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            var action = HuntFinderActionButton.Draw(_automation.Session.Active, false);
            if (action == HuntFinderAction.Stop) _automation.Session.Stop();
            ImGui.TextWrapped(_automation.Session.Message);
            if (_automation.Session.Active)
            {
                if (_automation.Session.TargetWorld is { } destination)
                    ImGui.TextUnformatted($"服务器：{destination.WorldName}（{_automation.Session.WorldProgress}/{_automation.Session.WorldCount}）");
                ImGui.TextUnformatted($"本图已发现：A {_automation.Session.FoundA} / B {_automation.Session.FoundB}");
                if (_automation.Session.ObservedARange > 0)
                    ImGui.TextDisabled($"本图 A 怪最远实测 {_automation.Session.ObservedARange:0} 米 · 扫描停靠 {_automation.Session.ApproachRadius:0} 米");
            }
            var monitor = _config.HuntDeathMonitoring;
            if (ImGui.Checkbox("自动监测怪物死亡", ref monitor)) { _config.HuntDeathMonitoring = monitor; _config.Save(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("仅监测当前所在服务器、地图、分线；离开加载范围不会直接判死。手动恢复存活优先。");
            ImGui.Separator();
            if (ImGui.BeginChild("FinderSettings", Vector2.Zero, false))
            {
                DrawFinder(action);
                if (_feedback.Length > 0) ImGui.TextWrapped(_feedback);
            }
            ImGui.EndChild();
        }
        ImGui.End();
    }

    private void DrawFinder(HuntFinderAction action)
    {
        ImGui.BeginDisabled(_automation.Session.Active);
        var rankA = _options.RankA; var rankB = _options.RankB;
        ImGui.Checkbox("A 怪", ref rankA); ImGui.SameLine(); ImGui.Checkbox("B 怪", ref rankB);
        _options.RankA = rankA; _options.RankB = rankB;
        var mount = _options.Mount; var fly = _options.Takeoff;
        ImGui.Checkbox("自动上坐骑", ref mount); ImGui.SameLine(); ImGui.Checkbox("可飞行时自动起飞", ref fly);
        _options.Mount = mount; _options.Takeoff = fly;
        var smart = _options.SmartTeleport;
        if (ImGui.Checkbox("智能水晶传送", ref smart))
        { _options.SmartTeleport = _config.HuntFinderSmartTeleport = smart; _config.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("比较直达与传送后移动的估计耗时；同图预计至少节省10秒且20%才传送，60秒内不重复同图传送。传送若改变分线将停止找怪。");
        ImGui.SameLine(); var overhead = _options.TeleportSeconds;
        ImGui.SetNextItemWidth(65 * ImGuiHelpers.GlobalScale);
        if (ImGui.InputFloat("读条/加载（秒）", ref overhead, 0, 0, "%.0f"))
        { _options.TeleportSeconds = _config.HuntFinderTeleportSeconds = float.IsFinite(overhead) ? Math.Clamp(overhead,10,120) : 25; _config.Save(); }
        var adaptive = _options.AdaptiveDetection;
        if (ImGui.Checkbox("自适应探测距离", ref adaptive))
        { _options.AdaptiveDetection = _config.HuntFinderAdaptiveDetection = adaptive; _config.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("本图本线按实际加载的A怪距离保守估计停靠距离，上限150米。已确认点位无需飞到脸上；远距未确认会等待后抵近补扫，不把空对象列表当作未刷新。");
        ImGui.SameLine(); var recorded = _options.UseRecordedHunts;
        if (ImGui.Checkbox("利用已有现场记录", ref recorded))
        { _options.UseRecordedHunts = _config.HuntFinderUseRecordedHunts = recorded; _config.Save(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("仅采用本服、本地图、本分线最近60分钟的现场存活记录；齐全则跳过该图，只有一只继续找另一只。Sonar、导入、未知时间和死亡记录不作为已扫依据。");

        var radius = _options.Radius; var wait = _options.WaitMs / 1000;
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        ImGui.SliderFloat("基础停靠距离（米）", ref radius, 10, 150, "%.0f");
        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);
        ImGui.SliderInt("点位等待（秒）", ref wait, 1, 30);
        _options.Radius = radius; _options.WaitMs = wait * 1000;
        DrawWorldSelection();
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
                if (ImGui.Checkbox($"{map.Expansion + 2}.0 · {map.Name}（预设 {map.Points.Count} 点）##Find{map.TerritoryId}", ref selected))
                { if (selected) _selectedMaps.Add(map.TerritoryId); else _selectedMaps.Remove(map.TerritoryId); }
            }
        }
        ImGui.EndChild();
        ImGui.EndDisabled();
        if (action == HuntFinderAction.Start)
        {
            var current = _automation.Context.Territory;
            var routes = _automation.Maps.Where(m => _selectedMaps.Contains(m.TerritoryId))
                .OrderByDescending(m => m.TerritoryId == current).ThenBy(m => m.Expansion).ThenBy(m => m.TerritoryId)
                ;
            _options.Worlds = _config.HuntFinderMultiWorld
                ? (_config.HuntFinderWorlds ??= new()).Select(id => _worldCatalog.FirstOrDefault(w => w.WorldID == id)).OfType<HuntTrainWorld>().ToList()
                : new();
            if (_config.HuntFinderMultiWorld && _options.Worlds.Count != _config.HuntFinderWorlds.Distinct().Count())
                _feedback = "部分已选服务器已不可用，请清空后重新勾选";
            else if (_config.HuntFinderMultiWorld && (!_options.RankA || _options.Worlds.Count == 0))
                _feedback = "多服务器找怪请勾选 A 怪，并按执行顺序勾选至少一个服务器";
            else { _feedback = string.Empty; _automation.Session.Start(routes, _options, Environment.TickCount64); }
        }
        ImGui.TextWrapped("按内置预设点位顺序巡查；已确认怪物无需继续抵近，远距未确认会补扫。水晶仅正常传送，移动使用 vnavmesh。");
        if (_automation.Session.CurrentRoute?.TerritoryId == 1190 && _automation.Session.Active)
            ImGui.TextDisabled("夏劳尼荒野：实际飞行时巡点目标上抬20米，减少贴地遭遇野怪");
    }

    private void DrawWorldSelection()
    {
        var enabled = _config.HuntFinderMultiWorld;
        if (ImGui.Checkbox("多服务器找怪（按勾选顺序）", ref enabled))
        { _config.HuntFinderMultiWorld = enabled; _config.Save(); }
        if (!enabled) return;
        ImGui.TextWrapped("需要下载并启用 Lifestream（国服 API 15 版）。请在卫月第三方库搜索安装；跨服能力以 Lifestream 和游戏限制为准。");
        if (ImGui.SmallButton("Lifestream 项目说明"))
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/NightmareXIV/Lifestream") { UseShellExecute = true });
        if (_automation.WorldTravelError.Length != 0) ImGui.TextWrapped(_automation.WorldTravelError);
        var current = _automation.Context.World;
        var now = Environment.TickCount64;
        if (current != 0 && (_catalogWorld != current || _worldCatalog.Count == 0) && now >= _nextCatalogRefresh)
        {
            _worldCatalog = MapHelpers.GetImportableWorlds(current); _catalogWorld = current;
            _nextCatalogRefresh = now + 3000;
        }
        if (ImGui.SmallButton("刷新服务器列表")) { _catalogWorld = 0; _nextCatalogRefresh = 0; }
        if (MapHelpers.WorldCatalogError.Length != 0) ImGui.TextWrapped(MapHelpers.WorldCatalogError);
        var order = _config.HuntFinderWorlds ??= new();
        if (_worldPicker.Draw(_worldCatalog,current,order,ImGuiHelpers.GlobalScale)) _config.Save();
    }
}
