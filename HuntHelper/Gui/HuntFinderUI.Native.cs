using HuntHelper.Gui.Native;
using HuntHelper.Managers.Automation;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace HuntHelper.Gui;

public sealed partial class HuntFinderUI
{
    private string? _nativeDataCenter;

    internal NativeFormSnapshot NativeSnapshot()
    {
        if (!_open || !NativePresentation) return NativeFormSnapshot.Hidden;
        var active = _automation.Session.Active;
        var dr = _options.RouteMode == PatrolRouteMode.DRCoverage;
        var maps = _automation.GetMaps(_options.RouteMode);
        var rows = new List<NativeFormRow> {
            new("mode", "寻路模式", NativeFormKind.Choice, dr ? "dr" : "preset", !active,
                new[] { new NativeFormChoice("dr", "DR 巡航"), new NativeFormChoice("preset", "预设点位") }),
        };
        var advanced = new List<NativeFormRow> {
            new("recorded", "跳过已找齐的地图", NativeFormKind.Check, _options.UseRecordedHunts.ToString(), !active,
                Tooltip: "仅使用本服、本图、本线最近 60 分钟的现场存活记录；Sonar 和导入记录不作为已扫依据。"),
            new("monitor", "自动监测怪物死亡", NativeFormKind.Check, _config.HuntDeathMonitoring.ToString()) };
        if (dr) advanced.Add(new("dr-info", "自动飞行 · 当前分线", NativeFormKind.Label,
            Tooltip: "400 米是 DR 算法假设，不是实测探测距离；使用正常水晶传送与 vnavmesh 飞行，不自动切换分线。"));
        else
        {
            advanced.AddRange(new[] {
                new NativeFormRow("rankA", "A 怪", NativeFormKind.Check, _options.RankA.ToString(), !active),
                new("rankB", "B 怪", NativeFormKind.Check, _options.RankB.ToString(), !active),
                new("mount", "自动上坐骑", NativeFormKind.Check, _options.Mount.ToString(), !active),
                new("takeoff", "可飞行时自动起飞", NativeFormKind.Check, _options.Takeoff.ToString(), !active),
                new("smart", "智能水晶传送", NativeFormKind.Check, _options.SmartTeleport.ToString(), !active),
                new("teleport-seconds", "读条 / 加载（秒）", NativeFormKind.Number, ((int)_options.TeleportSeconds).ToString(), !active, Min: 10, Max: 120),
                new("adaptive", "自适应探测距离", NativeFormKind.Check, _options.AdaptiveDetection.ToString(), !active),
                new("radius", "基础停靠距离（米）", NativeFormKind.Number, ((int)_options.Radius).ToString(), !active, Min: 10, Max: 150),
                new("wait", "点位等待（秒）", NativeFormKind.Number, (_options.WaitMs / 1000).ToString(), !active, Min: 1, Max: 30),
            });
        }
        rows.Add(new("multi", "多服找怪（按勾选顺序）", NativeFormKind.Check, _config.HuntFinderMultiWorld.ToString(), !active));
        if (_config.HuntFinderMultiWorld)
        {
            RefreshNativeWorlds();
            var groups = _worldCatalog.Select(w => w.DataCenterName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().ToList();
            _nativeDataCenter ??= _worldCatalog.FirstOrDefault(w => w.WorldID == _automation.Context.World)?.DataCenterName ?? "";
            if (_nativeDataCenter.Length > 0 && !groups.Contains(_nativeDataCenter)) _nativeDataCenter = "";
            var order = _config.HuntFinderWorlds ??= new();
            rows.Add(new("world-info", "跨服需要国服 Lifestream", NativeFormKind.Label, Tooltip: _automation.WorldTravelError));
            rows.Add(new("world-actions", "", NativeFormKind.Buttons, Enabled: !active, Choices: new[] {
                new NativeFormChoice("world-refresh", "刷新列表"), new NativeFormChoice("world-clear", "清空服务器"),
                new NativeFormChoice("lifestream", "下载说明") }));
            rows.Add(new("dc", "大区", NativeFormKind.Choice, _nativeDataCenter, !active,
                new[] { new NativeFormChoice("", "全部大区") }.Concat(groups.Select(g => new NativeFormChoice(g, g))).ToArray()));
            rows.Add(new("world", "服务器", NativeFormKind.CheckList, Enabled: !active, Choices:
                _worldCatalog.Where(w => _nativeDataCenter.Length == 0 || w.DataCenterName == _nativeDataCenter)
                    .Select(w => new NativeFormChoice(w.WorldID.ToString(), (order.IndexOf(w.WorldID) is var i && i >= 0 ? $"{i + 1}. " : "") + w.WorldName, order.Contains(w.WorldID))).ToArray()));
            rows.Add(new("queue", "执行顺序：" + (order.Count == 0 ? "请勾选服务器" : string.Join(" → ", order.Select(id => _worldCatalog.FirstOrDefault(w => w.WorldID == id)?.WorldName ?? $"区服{id}"))), NativeFormKind.Label));
            if (MapHelpers.WorldCatalogError.Length > 0) rows.Add(new("world-error", MapHelpers.WorldCatalogError, NativeFormKind.Label));
        }
        rows.Add(new("version", "版本", NativeFormKind.Choice, _expansion.ToString(), !active,
            new[] { new NativeFormChoice("-1", "全部版本") }.Concat(maps.Select(m => m.Expansion).Distinct().Order().Select(v => new NativeFormChoice(v.ToString(), $"{v + 2}.0"))).ToArray()));
        rows.Add(new("map-actions", "", NativeFormKind.Buttons, Enabled: !active,
            Choices: new[] { new NativeFormChoice("maps-all", "全选本版本"), new NativeFormChoice("maps-clear", "清空") }));
        rows.Add(new("map", $"地图 · 已选 {_selectedMaps.Count} 张", NativeFormKind.CheckList, Enabled: !active,
            Choices: maps.Where(m => _expansion < 0 || m.Expansion == _expansion)
                .Select(m => new NativeFormChoice(m.TerritoryId.ToString(), $"{m.Expansion + 2}.0 · {m.Name}", _selectedMaps.Contains(m.TerritoryId))).ToArray()));
        if (_compactOptions) rows.AddRange(advanced);
        var status = _feedback.Length > 0 ? _feedback : _automation.CoverageDataError.Length > 0 && dr ? _automation.CoverageDataError : _automation.Session.Message;
        if (active) status += $" · 已发现 A {_automation.Session.FoundA}" + (!dr ? $" / B {_automation.Session.FoundB}" : "");
        return new(true, new[] { new NativeFormChoice("start", active ? "停止找怪" : "开始找怪"), new NativeFormChoice("settings", "设置", _compactOptions), new NativeFormChoice("legacy", "原版窗口") }, rows, status);
    }

    private void RefreshNativeWorlds()
    {
        var current = _automation.Context.World; var now = Environment.TickCount64;
        if (current == 0 || now < _nextCatalogRefresh || _catalogWorld == current && _worldCatalog.Count > 0) return;
        _worldCatalog = MapHelpers.GetImportableWorlds(current); _catalogWorld = current; _nextCatalogRefresh = now + 3000;
    }

    internal void NativeDispatch(NativeFormRequest request)
    {
        if (request.Key == "close") { _open = false; return; }
        if (request.Key == "settings") { _compactOptions = !_compactOptions; return; }
        if (request.Key == "legacy") { _config.UseNativeHuntWindows = false; _config.Save(); return; }
        if (request.Key == "start") { if (_automation.Session.Active) _automation.Session.Stop(); else { RefreshNativeWorlds(); StartPatrol(); } return; }
        if (request.Key == "monitor") { _config.HuntDeathMonitoring = request.Value == "True"; _config.Save(); return; }
        if (_automation.Session.Active) return;
        var value = request.Value == "True";
        _ = int.TryParse(request.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number);
        switch (request.Key)
        {
            case "mode": SetRouteMode(request.Value == "dr" ? PatrolRouteMode.DRCoverage : PatrolRouteMode.Preset); break;
            case "version": _expansion = number; break;
            case "dc": _nativeDataCenter = request.Value; break;
            case "map": if (uint.TryParse(request.Value, out var map) && _automation.GetMaps(_options.RouteMode).Any(m => m.TerritoryId == map)) { if (!_selectedMaps.Add(map)) _selectedMaps.Remove(map); } break;
            case "maps-all": foreach (var route in _automation.GetMaps(_options.RouteMode).Where(m => _expansion < 0 || m.Expansion == _expansion)) _selectedMaps.Add(route.TerritoryId); break;
            case "maps-clear": _selectedMaps.Clear(); break;
            case "multi": _config.HuntFinderMultiWorld = value; _config.Save(); break;
            case "world":
                if (uint.TryParse(request.Value, out var world) && _worldCatalog.Any(w => w.WorldID == world))
                { var list = _config.HuntFinderWorlds ??= new(); if (!list.Remove(world)) list.Add(world); _config.Save(); } break;
            case "world-clear": (_config.HuntFinderWorlds ??= new()).Clear(); _config.Save(); break;
            case "world-refresh": _catalogWorld = 0; _nextCatalogRefresh = 0; RefreshNativeWorlds(); break;
            case "lifestream": System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/NightmareXIV/Lifestream") { UseShellExecute = true }); break;
            case "recorded": _options.UseRecordedHunts = _config.HuntFinderUseRecordedHunts = value; _config.Save(); break;
            case "rankA": _options.RankA = value; break;
            case "rankB": _options.RankB = value; break;
            case "mount": _options.Mount = value; break;
            case "takeoff": _options.Takeoff = value; break;
            case "smart": _options.SmartTeleport = _config.HuntFinderSmartTeleport = value; _config.Save(); break;
            case "adaptive": _options.AdaptiveDetection = _config.HuntFinderAdaptiveDetection = value; _config.Save(); break;
            case "teleport-seconds": _options.TeleportSeconds = _config.HuntFinderTeleportSeconds = Math.Clamp(number, 10, 120); _config.Save(); break;
            case "radius": _options.Radius = Math.Clamp(number, 10, 150); break;
            case "wait": _options.WaitMs = Math.Clamp(number, 1, 30) * 1000; break;
        }
    }
}
