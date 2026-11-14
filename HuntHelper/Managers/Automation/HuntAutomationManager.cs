using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Config;
using Dalamud.Plugin.Services;
using Dalamud.Plugin;
using Dalamud.Interface.Textures.TextureWraps;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Managers.MapData;
using HuntHelper.Utilities;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

public sealed class HuntAutomationManager : IPatrolHost, IDisposable
{
    private readonly IClientState _client;
    private readonly IPlayerState _player;
    private readonly IObjectTable _objects;
    private readonly ICondition _condition;
    private readonly IAetheryteList _aetherytes;
    private readonly IDataManager _data;
    private readonly TrainManager _trains;
    private readonly HuntManager _hunts;
    private readonly NavigationManager _navigation;
    private readonly Configuration _config;
    private readonly MapDataManager _mapData;
    private readonly Dictionary<uint, string> _previewImageNames;
    private DRPatrolPreview? _preview;
    private readonly IGameConfig _gameConfig;
    private readonly IFramework _framework;
    private readonly CoverageRangeLease _coverageRange;
    private System.Threading.Tasks.Task? _rangeRestoreTask;
    private readonly string _coverageDataPath;
    private IReadOnlyList<PatrolRoute>? _coverageMaps;
    public string CoverageDataError { get; private set; } = string.Empty;
    private readonly LifestreamWorldTravel _worldTravel;
    public string WorldTravelError => _worldTravel.Error;
    public bool WorldTravelBusy => _worldTravel.Busy;
    public string WorldVisitProblem(HuntTrainWorld world) => _worldTravel.VisitProblem(world);
    public bool ChangeWorld(HuntTrainWorld world, out string error) => _worldTravel.Change(world, out error);
    public void AbortWorldTravel() => _worldTravel.AbortOwnedRequest();
    private readonly HuntDeathMonitor _deaths = new();
    private readonly HuntDeathMonitor _sDeaths = new();
    private readonly TrainManager? _sTrains;
    private long _nextTick, _nextAction, _nextDeath, _takeoffUntil;
    private bool _disposed;
    public PatrolSession Session { get; }
    public DRPatrolPreview? Preview
    {
        get
        {
            if (_disposed || _preview is not { } preview || !Session.Active || Session.RouteMode != PatrolRouteMode.DRCoverage ||
                Session.State is not (PatrolState.Scanning or PatrolState.PreparingMove or PatrolState.Moving) ||
                Session.CurrentRoute is not { } route || route.TerritoryId != preview.Territory || route.MapId != preview.Map)
                return null;
            if (Session.State == PatrolState.Moving && _navigation.Interrupted) return null;
            var context = Context;
            return context.LoggedIn && context.Ready && !context.Dead && !context.InCombat && context.World != 0 && context.World == preview.World &&
                context.Territory == preview.Territory && context.Map == preview.Map && context.Instance == preview.Instance
                ? preview : null;
        }
    }
    public List<PatrolRoute> Maps { get; } = new();
    public bool Navigating => _navigation.OwnsMovement;
    public bool NavigationInterrupted => _navigation.Interrupted;
    public string NavigationMessage => _navigation.LastMessage;
    public PatrolContext Context => new(_player.CurrentWorld.RowId, _client.TerritoryType, _client.MapId,
        MapHelpers.CurrentInstance, _objects.LocalPlayer?.Position ?? Vector3.Zero,
        _client.IsLoggedIn,
        _player.IsLoaded && _objects.LocalPlayer != null && _client.TerritoryType != 0 && _client.MapId != 0 && !_condition[ConditionFlag.BetweenAreas] &&
        !_condition[ConditionFlag.BetweenAreas51] && !_condition[ConditionFlag.Casting],
        _condition[ConditionFlag.InCombat], _objects.LocalPlayer?.IsDead ?? false);

    public HuntAutomationManager(IClientState client, IPlayerState player, IObjectTable objects, ICondition condition,
        IAetheryteList aetherytes, IDataManager data, TrainManager trains, HuntManager hunts, NavigationManager navigation,
        MapDataManager maps, Configuration config, IDalamudPluginInterface pluginInterface, IGameConfig gameConfig, IFramework framework, TrainManager? sTrains = null)
    {
        _client = client; _player = player; _objects = objects; _condition = condition; _aetherytes = aetherytes;
        _data = data; _trains = trains; _hunts = hunts; _navigation = navigation; _config = config;
        _mapData = maps;
        _previewImageNames = maps.SpawnPointsList.GroupBy(map => (uint)map.MapID)
            .ToDictionary(group => group.Key, group => group.First().MapName);
        _gameConfig = gameConfig;
        _framework = framework;
        _coverageRange = new(ReadCoverageRange, ApplyCoverageRange);
        _gameConfig.SystemChanged += OnSystemConfigChanged;
        _coverageDataPath = Path.Combine(pluginInterface.AssemblyLocation.Directory!.FullName, "Data", "DRSpawnPoints.json");
        _worldTravel = new(pluginInterface);
        _sTrains = sTrains;
        if (_sTrains != null) _sTrains.ManualDeadStateChanged += _sDeaths.ManualChange;
        Session = new(this);
        _trains.ManualDeadStateChanged += _deaths.ManualChange;
        _trains.WorldDeleted += OnWorldDeleted;
        foreach (var map in maps.SpawnPointsList)
        {
            var zone = data.GetExcelSheet<TerritoryType>().GetRowOrDefault(map.MapID);
            if (zone == null || zone.Value.Map.RowId == 0) continue;
            Maps.Add(new PatrolRoute { TerritoryId = map.MapID, MapId = zone.Value.Map.RowId,
                Name = zone.Value.PlaceName.ValueNullable?.Name.ToString() ?? map.MapName,
                Expansion = (int)zone.Value.ExVersion.RowId,
                Points = map.Positions.Where(p => p.A || p.B).GroupBy(p => p.Position)
                    .Select(g => new PatrolPoint { MapPosition = g.Key, ForA = g.Any(p => p.A), ForB = g.Any(p => p.B) }).ToList() });
        }
    }

    private void OnWorldDeleted(uint id) { if (id == Context.World && Session.Active) Session.Stop("已删除当前服记录，停止找怪"); }

    public IReadOnlyList<PatrolRoute> GetMaps(PatrolRouteMode mode)
    {
        if (mode != PatrolRouteMode.DRCoverage) return Maps;
        if (_coverageMaps != null) return _coverageMaps;
        if (DRPatrolRoutes.TryLoad(_coverageDataPath, Maps, out var routes, out var error))
        {
            CoverageDataError = string.Empty;
            return _coverageMaps = routes;
        }
        CoverageDataError = error;
        return _coverageMaps = Array.Empty<PatrolRoute>();
    }

    public bool BeginCoverage(out string error)
    {
        if (_rangeRestoreTask is { IsCompleted: false })
        { error = "正在恢复上次找怪的显示范围，请稍后重试"; return false; }
        error = _navigation.GetCoverageDisabledReason();
        return error.Length == 0 && _coverageRange.TryAcquire(out error);
    }

    public void EndCoverage()
    {
        if (!_framework.IsInFrameworkUpdateThread)
        {
            // Unload can arrive outside the game thread. Keep the lease alive in
            // the queued action so restoring the runtime setting remains safe.
            if (_rangeRestoreTask == null || _rangeRestoreTask.IsCompleted)
                _rangeRestoreTask = _framework.RunOnFrameworkThread(RestoreCoverageRange);
            return;
        }
        RestoreCoverageRange();
    }

    private void RestoreCoverageRange()
    {
        try { _coverageRange.Release(); }
        catch (Exception ex) { PluginLog.Warning(ex, "Could not restore object display range; will retry while idle"); }
    }

    private uint? ReadCoverageRange() => _gameConfig.System.TryGetUInt(nameof(SystemConfigOption.DynamicAroundRangeMode), out var mode) ? mode : null;

    private static unsafe void ApplyCoverageRange(uint mode)
    {
        // The normal ConfigModule application path updates runtime state as well
        // as the stored option. IGameConfig.System.Set alone can defer its effect.
        var module = ConfigModule.Instance();
        if (module == null) throw new InvalidOperationException("游戏系统设置尚未就绪");
        for (var index = 0; index < module->Options.Length; index++)
        {
            if (module->Options[index].GetName() != nameof(SystemConfigOption.DynamicAroundRangeMode)) continue;
            if (!module->SetValueByIndex(index, checked((int)mode), 0, true, true))
                throw new InvalidOperationException("游戏无法应用角色与物体显示范围设置");
            return;
        }
        throw new InvalidOperationException("当前国服客户端未找到角色与物体显示范围设置");
    }

    private void OnSystemConfigChanged(object? sender, ConfigChangeEvent change)
    {
        if (change.Option is SystemConfigOption.DynamicAroundRangeMode ||
            change.Name == nameof(SystemConfigOption.DynamicAroundRangeMode))
            _coverageRange.NotifyExternalChange();
    }

    public bool TryCoverageDestination(Vector3 candidate, out Vector3 destination)
    {
        destination = default;
        if (!_condition[ConditionFlag.InFlight]) return false;
        candidate = PatrolTravelPlanner.FlightTarget(Context.Territory, candidate, true);
        return _navigation.TryResolveCoveragePoint(candidate, out destination);
    }

    public IReadOnlyList<PatrolCoverageCrystal> CoverageCrystals(PatrolRoute route)
    {
        var scale = MapScale(route);
        if (scale <= 0) return Array.Empty<PatrolCoverageCrystal>();
        return AvailableCrystals(route.TerritoryId).Where(c => c.Position.HasValue)
            .Select(c => new PatrolCoverageCrystal(c, PatrolTravelPlanner.Horizontal(NavigationRules.FromHuntMapCoordinates(c.Position!.Value, scale))))
            .Where(c => float.IsFinite(c.Position.X) && float.IsFinite(c.Position.Y)).ToList();
    }

    public float CoverageSpeed(PatrolOptions options) => TravelSpeed(options);

    public IDalamudTextureWrap? GetPreviewMapImage(uint territory)
    {
        if (Preview?.Territory != territory || !_previewImageNames.TryGetValue(territory, out var mapName)) return null;
        _hunts.EnsureMapImages(_mapData.SpawnPointsList);
        return _hunts.GetMapImage(mapName);
    }

    private void UpdatePreview()
    {
        var preview = Session.GetDRPreview(Context);
        if (preview == null) { _preview = null; return; }
        IReadOnlyList<Vector3> path = Array.Empty<Vector3>();
        var pathStatus = string.Empty;
        if (preview.State == PatrolState.Moving)
        {
            var remaining = _navigation.GetOwnedPath(out pathStatus);
            if (remaining.Count > 0)
            {
                var connected = new Vector3[remaining.Count + 1];
                connected[0] = preview.Player;
                for (var index = 0; index < remaining.Count; index++) connected[index + 1] = remaining[index];
                path = connected;
            }
        }
        _preview = preview with
        {
            WorldName = _trains.GetCurrentWorld().WorldName,
            MapScale = _hunts.GetMapZoneScale(preview.Territory),
            Path = path,
            PathStatus = pathStatus
        };
    }

    public void Tick()
    {
        if (_disposed || Environment.TickCount64 < _nextTick) return;
        var now = Environment.TickCount64; _nextTick = now + 250;
        try
        {
            if (Session.Active && _coverageRange.Active && (_coverageRange.ChangedExternally || ReadCoverageRange() != 2))
                Session.Stop("角色与物体显示范围已改变，DR 覆盖扫描已停止");
            if (!Session.Active && _coverageRange.Active) EndCoverage();
            Session.Tick(now);
            UpdatePreview();
            if ((_config.HuntDeathMonitoring || _config.HuntSDeathMonitoring) && now >= _nextDeath)
            {
                _nextDeath = now + 500;
                var observations = _objects.OfType<IBattleNpc>()
                    .Select(n => new HuntObservation(n.NameId, n.Position, n.CurrentHp, n.MaxHp)).ToList();
                if (_config.HuntDeathMonitoring && _deaths.Update(Context, _trains.GetAllTrainRecords(), observations, now))
                    _trains.SaveHuntTrainRecord();
                if (_config.HuntSDeathMonitoring && _sTrains != null && _sDeaths.Update(Context,_sTrains.GetAllTrainRecords(),observations,now))
                    _sTrains.SaveHuntTrainRecord();
            }
            if (!_config.HuntDeathMonitoring) _deaths.Reset();
            if (!_config.HuntSDeathMonitoring) _sDeaths.Reset();
        }
        catch (Exception ex)
        {
            _preview = null;
            Session.Stop($"找怪暂停：{ex.Message}");
            PluginLog.Warning(ex, "Hunt automation stopped");
        }
    }

    private IReadOnlyList<PatrolCrystal> AvailableCrystals(uint territory)
    {
        var unlocked = _aetherytes.Select(a => a.AetheryteId).ToHashSet();
        var positions = TeleportManager.GetPatrolCrystals(territory).ToDictionary(a => a.AetheryteID, a => a.Position);
        return _data.GetExcelSheet<Aetheryte>().Where(a => a.Territory.RowId == territory && a.IsAetheryte && unlocked.Contains(a.RowId))
            .Select(a => new PatrolCrystal(a.RowId, a.PlaceName.ValueNullable?.Name.ToString() ?? $"水晶 {a.RowId}",
                positions.TryGetValue(a.RowId, out var position) && position.X > 1 && position.Y > 1 ? position : null)).ToList();
    }

    private float MapScale(PatrolRoute route) => _data.GetExcelSheet<Lumina.Excel.Sheets.Map>().GetRowOrDefault(route.MapId)?.SizeFactor ?? 0;
    private float TravelSpeed(PatrolOptions options) => options.Mount || _condition[ConditionFlag.Mounted]
        ? (options.Takeoff && Control.GetFlightAllowedStatus() == Control.FlightAllowedStatus.CanFly || _condition[ConditionFlag.InFlight] ? 20 : 9) : 6;

    public PatrolTeleport? PlanTeleport(PatrolRoute route, PatrolPoint point, PatrolOptions options) =>
        PatrolTravelPlanner.Select(Context, route, point, options, AvailableCrystals(route.TerritoryId), MapScale(route), TravelSpeed(options));

    public bool ResolvePoint(PatrolRoute route, PatrolPoint point, out Vector3 destination)
    {
        destination = point.Position ?? default;
        if (!point.Position.HasValue && !_navigation.TryResolvePatrolPoint(route.MapId, point.MapPosition, out destination, out _)) return false;
        destination = PatrolTravelPlanner.FlightTarget(route.TerritoryId, destination, _condition[ConditionFlag.InFlight]);
        return true;
    }

    public IReadOnlyList<PatrolDetection> KnownDetections(bool rankA, bool rankB)
    {
        var c = Context; var now = DateTime.UtcNow;
        return _trains.GetTrainRecords(c.World).Where(m => PatrolTravelPlanner.RecentLivingScan(m, c, now) && _hunts.IsHunt(m.MobID))
            .Where(m => rankA && _hunts.GetHuntRank(m.MobID) == HuntRank.A || rankB && _hunts.GetHuntRank(m.MobID) == HuntRank.B)
            .Select(m => new PatrolDetection(m.MobID, _hunts.GetHuntRank(m.MobID) == HuntRank.A, m.WorldPosition)).ToList();
    }

    public bool Teleport(PatrolRoute route, out string error) => Teleport(route,
        PlanTeleport(route, route.Points[0], new PatrolOptions()), out error);

    public unsafe bool Teleport(PatrolRoute route, PatrolTeleport? plan, out string error)
    {
        error = string.Empty;
        if (!Context.Ready || Context.InCombat || Context.Dead) { error = "角色暂时无法传送"; return false; }
        if (plan == null || !AvailableCrystals(route.TerritoryId).Any(c => c.Id == plan.Crystal.Id))
        { error = $"{route.Name} 没有已解锁的可用水晶，请手动进入该地图后开始"; return false; }
        // Standard game teleport to an unlocked crystal. No arbitrary-coordinate teleport exists here.
        if (Telepo.Instance() == null || !Telepo.Instance()->Teleport(plan.Crystal.Id, 0))
        { error = "水晶传送未成功，可能正在移动或不可传送"; return false; }
        return true;
    }

    public unsafe bool PrepareMount(bool mount, bool takeoff, out string message)
    {
        var c = Context;
        var world = _trains.GetCurrentWorld();
        var probe = new HuntTrainMob("找怪扫描点", 0, c.Territory, c.Map, c.Instance, "", Vector2.Zero,
            DateTime.UtcNow, false, world.WorldID, world.WorldName, world.DataCenterName, c.Position);
        var navigationReason = _navigation.GetDisabledReason(probe);
        if (navigationReason.Length > 0) { message = $"等待导航就绪：{navigationReason}"; return false; }
        message = "等待坐骑准备";
        if (!mount) return true;
        if (_condition[ConditionFlag.Mounted])
        {
            if (!takeoff || _condition[ConditionFlag.InFlight]) return true;
            if (Environment.TickCount64 < _takeoffUntil) { message = "等待起飞状态确认"; return false; }
            if (Control.GetFlightAllowedStatus() != Control.FlightAllowedStatus.CanFly)
            {
                if (_coverageRange.Active) { message = "DR 覆盖模式需要当前地图已解锁飞行，请解锁后重试"; return false; }
                return true;
            }
            message = "正在起飞";
            if (Environment.TickCount64 >= _nextAction)
            {
                _nextAction = Environment.TickCount64 + 1500;
                _takeoffUntil = Environment.TickCount64 + 2500;
                if (ActionManager.Instance() != null) ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
            }
            return false;
        }
        if (Environment.TickCount64 >= _nextAction)
        { _nextAction = Environment.TickCount64 + 1500; if (ActionManager.Instance() != null) ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9); }
        return false;
    }

    public bool Move(PatrolRoute route, PatrolPoint point, out Vector3 destination, out string error)
    {
        destination = point.Position ?? default; error = string.Empty;
        if (!point.Position.HasValue && !_navigation.TryResolvePatrolPoint(route.MapId, point.MapPosition, out destination, out error)) return false;
        if (!point.IsObservationPoint)
            destination = PatrolTravelPlanner.FlightTarget(route.TerritoryId, destination, _condition[ConditionFlag.InFlight]);
        var world = _trains.GetCurrentWorld();
        var target = new HuntTrainMob("找怪扫描点", 0, route.TerritoryId, route.MapId, Context.Instance, route.Name,
            point.MapPosition, DateTime.UtcNow, false, world.WorldID, world.WorldName, world.DataCenterName, destination);
        if (_navigation.TryMoveTo(target)) return true;
        error = _navigation.LastMessage;
        return false;
    }

    public void StopNavigation() => _navigation.Stop();

    public IReadOnlyList<PatrolDetection> Scan(bool rankA, bool rankB)
    {
        var result = new List<PatrolDetection>();
        var c = Context;
        foreach (var npc in _objects.OfType<IBattleNpc>())
        {
            if (npc.CurrentHp == 0 || !_hunts.IsHunt(npc.NameId)) continue;
            var rank = _hunts.GetHuntRank(npc.NameId);
            if (!(rankA && rank == HuntRank.A) && !(rankB && rank == HuntRank.B)) continue;
            _trains.AddMob(npc, c.Territory, c.Map, c.Instance, MapHelpers.GetMapName(c.Territory), _hunts.GetMapZoneScale(c.Territory));
            result.Add(new PatrolDetection(npc.NameId, rank == HuntRank.A, npc.Position));
        }
        return result;
    }

    public void Dispose()
    {
        _disposed = true;
        _preview = null;
        Session.Stop("插件已卸载");
        EndCoverage();
        _gameConfig.SystemChanged -= OnSystemConfigChanged;
        _trains.ManualDeadStateChanged -= _deaths.ManualChange;
        if (_sTrains != null) _sTrains.ManualDeadStateChanged -= _sDeaths.ManualChange;
        _trains.WorldDeleted -= OnWorldDeleted;
    }
}
