using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Managers.MapData;
using HuntHelper.Utilities;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private readonly HuntDeathMonitor _deaths = new();
    private readonly HuntDeathMonitor _sDeaths = new();
    private readonly TrainManager? _sTrains;
    private long _nextTick, _nextAction, _nextDeath, _takeoffUntil;
    private uint _recordWorld, _recordTerritory;
    private bool _disposed;
    public PatrolSession Session { get; }
    public PatrolRouteStore Store { get; }
    public List<PatrolRoute> Maps { get; } = new();
    public bool Recording { get; private set; }
    public string RecorderMessage { get; private set; } = "每张地图可录制一条三维路线";
    public bool AutomaticRecording { get; set; }
    public bool Navigating => _navigation.OwnsMovement;
    public string NavigationMessage => _navigation.LastMessage;
    public PatrolContext Context => new(_player.CurrentWorld.RowId, _client.TerritoryType, _client.MapId,
        MapHelpers.CurrentInstance, _objects.LocalPlayer?.Position ?? Vector3.Zero,
        _client.IsLoggedIn,
        _player.IsLoaded && _objects.LocalPlayer != null && _client.TerritoryType != 0 && _client.MapId != 0 && !_condition[ConditionFlag.BetweenAreas] &&
        !_condition[ConditionFlag.BetweenAreas51] && !_condition[ConditionFlag.Casting],
        _condition[ConditionFlag.InCombat], _objects.LocalPlayer?.IsDead ?? false);

    public HuntAutomationManager(IClientState client, IPlayerState player, IObjectTable objects, ICondition condition,
        IAetheryteList aetherytes, IDataManager data, TrainManager trains, HuntManager hunts, NavigationManager navigation,
        MapDataManager maps, Configuration config, string folder, TrainManager? sTrains = null)
    {
        _client = client; _player = player; _objects = objects; _condition = condition; _aetherytes = aetherytes;
        _data = data; _trains = trains; _hunts = hunts; _navigation = navigation; _config = config;
        _sTrains = sTrains;
        if (_sTrains != null) _sTrains.ManualDeadStateChanged += _sDeaths.ManualChange;
        Store = new(Path.Combine(folder, "ScanRoutes.json"));
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
                Points = map.Positions.Where(p => p.A || p.B).Select(p => new PatrolPoint { MapPosition = p.Position })
                    .DistinctBy(p => p.MapPosition).ToList() });
        }
    }

    public PatrolRoute GetRoute(uint territory, bool custom) =>
        (custom ? Store.Routes.FirstOrDefault(r => r.TerritoryId == territory && r.Points.Count > 0) : null)
        ?? Maps.First(r => r.TerritoryId == territory);

    private void OnWorldDeleted(uint id) { if (id == Context.World && Session.Active) Session.Stop("已删除当前服记录，停止找怪"); }

    public void Tick()
    {
        if (_disposed || Environment.TickCount64 < _nextTick) return;
        var now = Environment.TickCount64; _nextTick = now + 250;
        try
        {
            if (Recording)
            {
                var c = Context;
                if (!c.LoggedIn || c.World != _recordWorld || c.Territory != _recordTerritory)
                { Recording = false; RecorderMessage = "区域或服务器改变，已停止录制"; }
                else if (AutomaticRecording && c.Ready && !c.Dead) AddPoint(30);
            }
            Session.Tick(now);
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
            Session.Stop($"找怪暂停：{ex.Message}");
            Recording = false;
            PluginLog.Warning(ex, "Hunt automation stopped");
        }
    }

    public void ToggleRecording()
    {
        if (Recording) { Recording = false; RecorderMessage = "路线已保存"; return; }
        var c = Context;
        if (Session.Active || !c.Ready || !c.LoggedIn || c.World == 0 || !Maps.Any(m => m.TerritoryId == c.Territory))
        { RecorderMessage = "请先停止找怪并进入可狩猎地图"; return; }
        _recordWorld = c.World; _recordTerritory = c.Territory;
        if (!Store.Routes.Any(r => r.TerritoryId == c.Territory))
        {
            var map = Maps.First(m => m.TerritoryId == c.Territory);
            Store.Put(new PatrolRoute { TerritoryId = map.TerritoryId, MapId = map.MapId, Name = map.Name, Expansion = map.Expansion });
        }
        Recording = true; AddPoint(10);
    }

    public bool AddPoint(float minimumDistance = 10)
    {
        var c = Context;
        if (!Recording || !c.LoggedIn || !c.Ready || c.World != _recordWorld || c.Territory != _recordTerritory) return false;
        var route = Store.Routes.First(r => r.TerritoryId == c.Territory);
        if (route.Points.Count >= 2000) { Recording = false; RecorderMessage = "点位达到上限 2000"; return false; }
        if (route.Points.LastOrDefault()?.Position is { } last && Vector3.Distance(last, c.Position) < minimumDistance)
        { if (minimumDistance < 30) RecorderMessage = "与上一点距离不足 10 米"; return false; }
        route.Points.Add(new PatrolPoint { Position = c.Position,
            MapPosition = MapHelpers.ConvertToMapCoordinate(c.Position, _hunts.GetMapZoneScale(c.Territory)) });
        RecorderMessage = Store.Save() ? $"已保存 {route.Points.Count} 个三维点" : $"保存失败：{Store.Error}";
        return true;
    }

    public unsafe bool Teleport(PatrolRoute route, out string error)
    {
        error = string.Empty;
        if (!Context.Ready || Context.InCombat || Context.Dead) { error = "角色暂时无法传送"; return false; }
        var unlocked = _aetherytes.Select(a => a.AetheryteId).ToHashSet();
        var crystal = _data.GetExcelSheet<Aetheryte>().FirstOrDefault(a => a.Territory.RowId == route.TerritoryId &&
            a.IsAetheryte && unlocked.Contains(a.RowId));
        if (crystal.RowId == 0) { error = $"{route.Name} 没有已解锁的可用水晶，请手动进入该地图后开始"; return false; }
        // Standard game teleport to an unlocked crystal. No arbitrary-coordinate teleport exists here.
        if (Telepo.Instance() == null || !Telepo.Instance()->Teleport(crystal.RowId, 0))
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
            if (Control.GetFlightAllowedStatus() != Control.FlightAllowedStatus.CanFly) return true;
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
            result.Add(new PatrolDetection(npc.NameId, rank == HuntRank.A));
        }
        return result;
    }

    public void Dispose()
    {
        _disposed = true; Recording = false;
        Session.Stop("插件已卸载");
        _trains.ManualDeadStateChanged -= _deaths.ManualChange;
        if (_sTrains != null) _sTrains.ManualDeadStateChanged -= _sDeaths.ManualChange;
        _trains.WorldDeleted -= OnWorldDeleted;
    }
}
