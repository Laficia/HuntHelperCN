using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HuntHelper.Managers.Hunts.Models;

namespace HuntHelper.Managers.Automation;

public enum PatrolState { Stopped, PreparingWorld, ChangingWorld, PreparingMap, Teleporting, PreparingMove, Moving, Scanning, Completed }

// A UI-thread state machine. Only normal crystal teleports and owned vnavmesh movement.
public sealed class PatrolSession
{
    private readonly IPatrolHost _host;
    private List<PatrolRoute> _routes = new();
    private PatrolOptions _options = new();
    private readonly HashSet<uint> _foundA = new(), _foundB = new();
    private readonly HashSet<int> _visited = new();
    private readonly Dictionary<(uint, bool), PatrolDetection> _detections = new();
    private uint _world, _instance;
    private int _route;
    private long _deadline, _scanUntil, _lastTeleportAt;
    private bool _ownsNavigation, _travelConsidered, _forceClose, _extendedStop;
    private bool _sameMapTeleport, _teleportBusySeen;
    private PatrolTeleport? _teleportPlan;
    private Vector3 _target;
    private List<HuntTrainWorld> _worlds = new();
    private int _worldIndex;
    private bool _ownsWorldTravel, _worldTravelBusySeen;
    private long _worldTravelStarted;
    public HuntTrainWorld? TargetWorld => _worlds.Count > _worldIndex ? _worlds[_worldIndex] : null;
    public int WorldProgress => _worlds.Count == 0 ? 0 : _worldIndex + 1;
    public int WorldCount => _worlds.Count;
    public PatrolState State { get; private set; }
    public string Message { get; private set; } = "未开始找怪";
    public bool Active => State is not (PatrolState.Stopped or PatrolState.Completed);
    public int PointIndex { get; private set; }
    public int CompletedPoints => _visited.Count;
    public PatrolRoute? CurrentRoute => _route < _routes.Count ? _routes[_route] : null;
    public int FoundA => _foundA.Count;
    public int FoundB => _foundB.Count;
    public float ObservedARange { get; private set; }
    public float ApproachRadius => _options.AdaptiveDetection && !_forceClose
        ? Math.Max(_options.Radius, Math.Min(150, ObservedARange * .7f - 10)) : _options.Radius;

    public PatrolSession(IPatrolHost host) => _host = host;

    public bool Start(IEnumerable<PatrolRoute> routes, PatrolOptions options, long now)
    {
        if (Active) return false;
        var context = _host.Context;
        var copy = routes.Where(r => r.Points.Any(p => options.RankA && p.ForA || options.RankB && p.ForB)).ToList();
        var worlds = options.Worlds.DistinctBy(w => w.WorldID).ToList();
        if (worlds.Count > 80 || worlds.Any(w => w.WorldID == 0 || string.IsNullOrWhiteSpace(w.WorldName)))
        { Message = "请选择有效服务器"; return false; }
        if (worlds.Any(w => w.WorldID != context.World))
        {
            if (_host.WorldTravelError.Length != 0) { Message = _host.WorldTravelError; return false; }
            if (_host.WorldTravelBusy) { Message = "Lifestream 正在执行其他任务，请先等待其完成"; return false; }
            foreach (var world in worlds.Where(w => w.WorldID != context.World))
            { var reason = _host.WorldVisitProblem(world); if (reason.Length != 0) { Message = reason; return false; } }
        }
        if (!context.LoggedIn || !context.Ready || context.World == 0 || context.Dead || context.InCombat ||
            _host.Navigating || copy.Count == 0 || !PatrolRouteRules.Valid(copy) || (!options.RankA && !options.RankB))
        { Message = "请等待角色就绪、选择地图和 A/B 等级，并停止已有移动"; return false; }
        _routes = copy.Select(r => new PatrolRoute { TerritoryId = r.TerritoryId, MapId = r.MapId,
            Expansion = r.Expansion, Name = r.Name,
            Points = r.Points.Where(p => options.RankA && p.ForA || options.RankB && p.ForB)
                .Select(p => new PatrolPoint { Position = p.Position, MapPosition = p.MapPosition, ForA = p.ForA, ForB = p.ForB }).ToList() }).ToList();
        _options = new() { RankA = options.RankA, RankB = options.RankB, Mount = options.Mount, Takeoff = options.Takeoff,
            SmartTeleport = options.SmartTeleport, AdaptiveDetection = options.AdaptiveDetection,
            UseRecordedHunts = options.UseRecordedHunts, TeleportSeconds = options.TeleportSeconds,
            Radius = Math.Clamp(options.Radius, 10, 150), WaitMs = Math.Clamp(options.WaitMs, 1000, 30000) };
        _world = context.World; _route = 0; PointIndex = 0; _ownsNavigation = false; _lastTeleportAt = now - 60000;
        _worlds = worlds; _worldIndex = 0; _ownsWorldTravel = false; _worldTravelBusySeen = false;
        ResetMap();
        State = worlds.Count == 0 ? PatrolState.PreparingMap : PatrolState.PreparingWorld; _deadline = now + 120000;
        Message = "准备开始找怪，优先核对本服本线近期现场记录";
        return true;
    }

    public void Stop(string reason = "已停止找怪")
    {
        if (_ownsNavigation) _host.StopNavigation();
        if (_ownsWorldTravel) _host.AbortWorldTravel();
        _ownsWorldTravel = false;
        _ownsNavigation = false; State = PatrolState.Stopped; Message = reason;
    }

    public void Tick(long now)
    {
        if (!Active) return;
        var c = _host.Context;
        if (State is PatrolState.PreparingWorld or PatrolState.ChangingWorld) { TickWorld(c, now); return; }
        if (!c.LoggedIn || (c.World != 0 && c.World != _world)) { Stop("区服改变或退出登录，已停止找怪"); return; }
        if (c.Dead || c.InCombat) { Stop("角色死亡或进入战斗，已停止找怪"); return; }
        var route = CurrentRoute!;
        if (now >= _deadline) { Stop("找怪步骤超时，请检查传送、坐骑或导航路线"); return; }
        if (State == PatrolState.Teleporting) _teleportBusySeen |= !c.Ready;
        if (!c.Ready || c.World == 0) return;
        if (State is not (PatrolState.PreparingMap or PatrolState.Teleporting) &&
            (c.Territory != route.TerritoryId || c.Map != route.MapId || c.Instance != _instance))
        { Stop("地图或分线改变，已停止当前巡点"); return; }

        // Same-map teleport cannot finish merely because the old map ID is still visible.
        if (State == PatrolState.Teleporting)
        {
            if (c.Territory != route.TerritoryId || c.Map != route.MapId) return;
            if (_sameMapTeleport)
            {
                if (c.Instance != _instance) { Stop("水晶传送后分线改变，已停止，避免混用分线记录"); return; }
                if (!_teleportBusySeen || _teleportPlan?.WorldPosition is not { } crystal ||
                    Vector2.Distance(PatrolTravelPlanner.Horizontal(c.Position), crystal) > 100) return;
                State = PatrolState.PreparingMove; _deadline = now + 120000;
            }
            else EnterMap(c, now);
            return;
        }

        if (c.Territory == route.TerritoryId && c.Map == route.MapId)
        {
            if (_options.UseRecordedHunts)
                Remember(_host.KnownDetections(_options.RankA, _options.RankB), c, false);
            if (Enough(route)) { NextMap(now, "本图近期存活记录已齐全，跳过重复巡点"); return; }
            Remember(_host.Scan(_options.RankA, _options.RankB), c, true);
            if (Enough(route)) { NextMap(now, "本图已探测齐全，转往下一地图"); return; }
        }

        switch (State)
        {
            case PatrolState.PreparingMap:
                ChoosePoint(route);
                if (c.Territory == route.TerritoryId && c.Map == route.MapId) EnterMap(c, now);
                else BeginTeleport(route, _host.PlanTeleport(route, route.Points[PointIndex], _options), false, now);
                break;
            case PatrolState.PreparingMove:
                if (!_travelConsidered)
                {
                    _travelConsidered = true;
                    if (_options.SmartTeleport && now - _lastTeleportAt >= 60000 &&
                        _host.PlanTeleport(route, route.Points[PointIndex], _options) is { } plan)
                    { BeginTeleport(route, plan, true, now); return; }
                }
                if (!_host.PrepareMount(_options.Mount, _options.Takeoff, out var prepare)) { Message = prepare; return; }
                if (_host.ResolvePoint(route, route.Points[PointIndex], out var resolved) && PointConfirmed(resolved))
                { _target = resolved; BeginScan(route, c, now); return; }
                if (!_host.Move(route, route.Points[PointIndex], out _target, out var moveError))
                { Stop($"扫描点 {PointIndex + 1} 无法寻路：{moveError}"); return; }
                _ownsNavigation = true; State = PatrolState.Moving; _deadline = now + 180000;
                Message = $"{route.Name}：前往扫描点 {PointIndex + 1}，已处理 {_visited.Count}/{route.Points.Count}";
                break;
            case PatrolState.Moving:
                if (PointConfirmed(_target) || Vector3.Distance(c.Position, _target) <= ApproachRadius)
                    BeginScan(route, c, now);
                else if (!_host.Navigating) Stop($"未到达扫描点：{_host.NavigationMessage}");
                break;
            case PatrolState.Scanning:
                if (now < _scanUntil) break;
                if (_extendedStop && !PointConfirmed(_target))
                {
                    // An empty object table at the learned distance does not prove absence.
                    _forceClose = true; State = PatrolState.PreparingMove; _deadline = now + 120000;
                    Message = "远距等待后仍未确认该点怪物，继续抵近补扫";
                    break;
                }
                _visited.Add(PointIndex);
                if (_visited.Count >= route.Points.Count) NextMap(now);
                else { ChoosePoint(route); State = PatrolState.PreparingMove; _deadline = now + 120000; }
                break;
        }
    }

    private bool Enough(PatrolRoute route)
    {
        var expected = route.Expansion == 0 ? 1 : 2;
        return (!_options.RankA || _foundA.Count >= expected) && (!_options.RankB || _foundB.Count >= expected);
    }
    private void Remember(IEnumerable<PatrolDetection> detections, PatrolContext c, bool live)
    {
        foreach (var found in detections)
        {
            if (found.Id == 0 || (found.IsA ? !_options.RankA : !_options.RankB)) continue;
            (found.IsA ? _foundA : _foundB).Add(found.Id);
            if (found.Position is not { } p || !Utilities.NavigationRules.IsFinite(p)) continue;
            // Old records seed the species count, but cannot prove that nearby unknown
            // spawn locations are currently loaded. Only this run's live view can do that.
            if (live) _detections[(found.Id, found.IsA)] = found;
            if (live && found.IsA) ObservedARange = Math.Max(ObservedARange, Vector3.Distance(c.Position, p));
        }
    }
    private bool PointConfirmed(Vector3 point) =>
        (!_options.RankA || _detections.Values.Any(d => d.IsA && d.Position is { } p && Vector3.Distance(p, point) <= Math.Min(25, _options.Radius))) &&
        (!_options.RankB || _detections.Values.Any(d => !d.IsA && d.Position is { } p && Vector3.Distance(p, point) <= Math.Min(25, _options.Radius)));
    private void BeginScan(PatrolRoute route, PatrolContext c, long now)
    {
        if (_ownsNavigation) _host.StopNavigation();
        _ownsNavigation = false;
        _extendedStop = Vector3.Distance(c.Position, _target) > _options.Radius && !PointConfirmed(_target);
        State = PatrolState.Scanning; _deadline = now + _options.WaitMs + 10000; _scanUntil = now + _options.WaitMs;
        Message = PointConfirmed(_target) ? "已确认该点附近怪物，无需继续飞到点位；等待补充加载" :
            $"{route.Name}：距扫描点 {Vector3.Distance(c.Position, _target):0} 米，等待怪物加载";
    }
    private void BeginTeleport(PatrolRoute route, PatrolTeleport? plan, bool sameMap, long now)
    {
        _sameMapTeleport = sameMap; _teleportPlan = plan; _teleportBusySeen = false;
        if (!_host.Teleport(route, plan, out var error)) { Stop(error); return; }
        _lastTeleportAt = now; State = PatrolState.Teleporting; _deadline = now + 120000;
        Message = plan == null ? $"正在正常传送至：{route.Name}" : sameMap
            ? $"传送至{plan.Crystal.Name}后继续巡点，预计节省 {plan.DirectSeconds - plan.ViaSeconds:0} 秒"
            : $"正常传送至{plan.Crystal.Name}，靠近下一扫描点";
    }
    private void ChoosePoint(PatrolRoute route)
    {
        var remaining = Enumerable.Range(0, route.Points.Count).Where(i => !_visited.Contains(i));
        PointIndex = remaining.First();
        _travelConsidered = false; _forceClose = false; _extendedStop = false;
    }
    private void EnterMap(PatrolContext c, long now)
    { _instance = c.Instance; State = PatrolState.PreparingMove; _deadline = now + 120000; }
    private void ResetMap()
    { _foundA.Clear(); _foundB.Clear(); _detections.Clear(); _visited.Clear(); ObservedARange = 0; _travelConsidered = false; _forceClose = false; _extendedStop = false; }
    private void NextMap(long now, string? reason = null)
    {
        if (_ownsNavigation) _host.StopNavigation();
        _ownsNavigation = false; _route++; PointIndex = 0; ResetMap();
        if (_route >= _routes.Count && _worldIndex + 1 < _worlds.Count)
        {
            _worldIndex++; _route = 0; PointIndex = 0;
            State = PatrolState.PreparingWorld; _deadline = now + 120000;
            Message = $"本服所选地图已完成，准备前往 {TargetWorld!.WorldName}";
        }
        else if (_route >= _routes.Count) { State = PatrolState.Completed; Message = reason ?? "所选地图巡点完成，结果已按实际所在服务器保存"; }
        else { State = PatrolState.PreparingMap; _deadline = now + 120000; Message = reason ?? "准备下一张地图"; }
    }

    private void TickWorld(PatrolContext c, long now)
    {
        var target = TargetWorld!;
        if (now >= _deadline) { Stop("Lifestream 跨服等待超时，已停止找怪"); return; }
        if (c.Dead || c.InCombat) { Stop("角色死亡或进入战斗，已停止跨服找怪"); return; }
        if (State == PatrolState.PreparingWorld)
        {
            if (!c.LoggedIn) { Stop("角色已退出登录，已停止找怪"); return; }
            if (!c.Ready || c.World == 0) return;
            if (c.World == target.WorldID) { _world = c.World; State = PatrolState.PreparingMap; _deadline = now + 120000; return; }
            if (_host.WorldTravelError.Length != 0) { Stop(_host.WorldTravelError); return; }
            if (!_host.ChangeWorld(target, out var error)) { Stop(error); return; }
            _ownsWorldTravel = true; _worldTravelStarted = now; _worldTravelBusySeen = false;
            State = PatrolState.ChangingWorld; _deadline = now + 600000;
            Message = $"Lifestream 正在前往 {target.WorldName}（{WorldProgress}/{WorldCount}），确认到服后再扫描";
            return;
        }
        if (_host.WorldTravelError.Length != 0) { Stop(_host.WorldTravelError); return; }
        var busy = _host.WorldTravelBusy;
        _worldTravelBusySeen |= busy;
        // Cross-DC Lifestream travel can legitimately pass through logout and intermediate worlds.
        if (busy) return;
        if (c.LoggedIn && c.Ready && c.World == target.WorldID)
        {
            _ownsWorldTravel = false; _world = c.World; State = PatrolState.PreparingMap; _deadline = now + 120000;
            Message = $"已确认到达 {target.WorldName}，开始所选地图预设路线";
        }
        else if (now - _worldTravelStarted > (_worldTravelBusySeen ? 3000 : 10000))
            Stop($"Lifestream 未到达 {target.WorldName}，已停止，未在其他服务器继续扫描");
    }
}
