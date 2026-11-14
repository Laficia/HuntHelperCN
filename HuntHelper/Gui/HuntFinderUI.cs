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

public sealed partial class HuntFinderUI
{
    internal bool NativePresentation { get; set; }
    internal Action? OpenNativeUi { get; set; }
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
        _options.RouteMode = config.HuntFinderRouteMode == PatrolRouteMode.DRCoverage
            ? PatrolRouteMode.DRCoverage : PatrolRouteMode.Preset;
        if (_options.RouteMode == PatrolRouteMode.DRCoverage)
        { _options.RankA = true; _options.RankB = false; _options.Mount = true; _options.Takeoff = true; }
    }
    public void Open()
    {
        _open = true;
        if (_selectedMaps.Count == 0 && _automation.GetMaps(_options.RouteMode).Any(m => m.TerritoryId == _automation.Context.Territory))
            _selectedMaps.Add(_automation.Context.Territory);
    }

    private void StartPatrol()
    {
        var current = _automation.Context.Territory;
        var routes = _automation.GetMaps(_options.RouteMode).Where(m => _selectedMaps.Contains(m.TerritoryId))
            .OrderByDescending(m => m.TerritoryId == current).ThenBy(m => m.Expansion).ThenBy(m => m.TerritoryId);
        _options.Worlds = _config.HuntFinderMultiWorld
            ? (_config.HuntFinderWorlds ??= new()).Select(id => _worldCatalog.FirstOrDefault(w => w.WorldID == id)).OfType<HuntTrainWorld>().ToList() : new();
        if (_options.RouteMode == PatrolRouteMode.DRCoverage && _automation.CoverageDataError.Length > 0) _feedback = _automation.CoverageDataError;
        else if (_config.HuntFinderMultiWorld && _options.Worlds.Count != _config.HuntFinderWorlds.Distinct().Count())
            _feedback = "部分已选服务器已不可用，请清空后重新勾选";
        else if (_config.HuntFinderMultiWorld && (!_options.RankA || _options.Worlds.Count == 0))
            _feedback = "多服务器找怪请勾选 A 怪，并按执行顺序勾选至少一个服务器";
        else { _feedback = string.Empty; _automation.Session.Start(routes, _options, Environment.TickCount64); }
    }

    private void SetRouteMode(PatrolRouteMode mode)
    {
        if (_automation.Session.Active || _options.RouteMode == mode) return;
        _options.RouteMode = _config.HuntFinderRouteMode = mode;
        if (mode == PatrolRouteMode.DRCoverage)
        { _options.RankA = true; _options.RankB = false; _options.Mount = true; _options.Takeoff = true; }
        var maps = _automation.GetMaps(mode);
        _selectedMaps.IntersectWith(maps.Select(map => map.TerritoryId));
        if (_selectedMaps.Count == 0 && maps.Any(map => map.TerritoryId == _automation.Context.Territory))
            _selectedMaps.Add(_automation.Context.Territory);
        if (_expansion >= 0 && !maps.Any(map => map.Expansion == _expansion)) _expansion = -1;
        _feedback = string.Empty;
        _config.Save();
    }

    private void DrawWorldSelection()
    {
        var enabled = _config.HuntFinderMultiWorld;
        if (ImGui.Checkbox("多服找怪（按勾选顺序）", ref enabled))
        { _config.HuntFinderMultiWorld = enabled; _config.Save(); }
        if (!enabled) return;
        ImGui.TextWrapped("需安装国服 Lifestream。");
        if (ImGui.SmallButton("下载说明"))
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
