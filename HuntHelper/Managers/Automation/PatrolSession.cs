using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

public enum PatrolState { Stopped, PreparingMap, Teleporting, PreparingMove, Moving, Scanning, Completed }

// A UI-thread state machine. No continuations, coordinates writes, or chat commands can start movement.
public sealed class PatrolSession
{
    private readonly IPatrolHost _host;
    private List<PatrolRoute> _routes = new();
    private PatrolOptions _options = new();
    private readonly HashSet<uint> _foundA = new(), _foundB = new();
    private uint _world, _instance;
    private int _route;
    private long _deadline;
    private bool _ownsNavigation;
    private Vector3 _target;
    public PatrolState State { get; private set; }
    public string Message { get; private set; } = "未开始找怪";
    public bool Active => State is not (PatrolState.Stopped or PatrolState.Completed);
    public int PointIndex { get; private set; }
    public PatrolRoute? CurrentRoute => _route < _routes.Count ? _routes[_route] : null;
    public int FoundA => _foundA.Count;
    public int FoundB => _foundB.Count;

    public PatrolSession(IPatrolHost host) => _host = host;

    public bool Start(IEnumerable<PatrolRoute> routes, PatrolOptions options, long now)
    {
        if (Active) return false;
        var context = _host.Context;
        var copy = routes.Where(r => r.Points.Count > 0).ToList();
        if (!context.LoggedIn || !context.Ready || context.World == 0 || context.Dead || context.InCombat ||
            _host.Navigating || copy.Count == 0 || !PatrolRouteStore.Valid(copy) || (!options.RankA && !options.RankB))
        { Message = "请等待角色就绪、选择地图和 A/B 等级，并停止已有移动"; return false; }
        // Deep snapshot: editing/importing a route cannot mutate a running scan.
        _routes = copy.Select(r => new PatrolRoute { TerritoryId = r.TerritoryId, MapId = r.MapId,
            Expansion = r.Expansion, Name = r.Name, Points = r.Points.Select(p => new PatrolPoint
                { Position = p.Position, MapPosition = p.MapPosition }).ToList() }).ToList();
        _options = new() { RankA = options.RankA, RankB = options.RankB, Mount = options.Mount, Takeoff = options.Takeoff,
            Radius = Math.Clamp(options.Radius, 10, 150), WaitMs = Math.Clamp(options.WaitMs, 1000, 30000) };
        _world = context.World; _route = 0; PointIndex = 0; _ownsNavigation = false;
        _foundA.Clear(); _foundB.Clear();
        State = PatrolState.PreparingMap; _deadline = now + 120000;
        Message = "准备开始找怪，结果保存到当前所在服务器";
        return true;
    }

    public void Stop(string reason = "已停止找怪")
    {
        if (_ownsNavigation) _host.StopNavigation();
        _ownsNavigation = false; State = PatrolState.Stopped; Message = reason;
    }

    public void Tick(long now)
    {
        if (!Active) return;
        var c = _host.Context;
        if (!c.LoggedIn || (c.World != 0 && c.World != _world)) { Stop("区服改变或退出登录，已停止找怪"); return; }
        if (c.Dead || c.InCombat) { Stop("角色死亡或进入战斗，已停止找怪"); return; }
        var route = CurrentRoute!;
        if (now >= _deadline) { Stop("找怪步骤超时，请检查传送、坐骑或导航路线"); return; }
        if (!c.Ready || c.World == 0) return;
        if (State is not (PatrolState.PreparingMap or PatrolState.Teleporting) &&
            (c.Territory != route.TerritoryId || c.Map != route.MapId || c.Instance != _instance))
        { Stop("地图或分线改变，已停止当前巡点"); return; }

        if (c.Territory == route.TerritoryId && c.Map == route.MapId)
        {
            foreach (var found in _host.Scan(_options.RankA, _options.RankB))
                (found.IsA ? _foundA : _foundB).Add(found.Id);
            var expected = route.Expansion == 0 ? 1 : 2;
            if ((!_options.RankA || _foundA.Count >= expected) && (!_options.RankB || _foundB.Count >= expected))
            { NextMap(now); return; }
        }

        switch (State)
        {
            case PatrolState.PreparingMap:
                if (c.Territory == route.TerritoryId && c.Map == route.MapId) EnterMap(c, now);
                else if (_host.Teleport(route, out var error))
                { State = PatrolState.Teleporting; _deadline = now + 120000; Message = $"正在正常传送至：{route.Name}"; }
                else Stop(error);
                break;
            case PatrolState.Teleporting:
                if (c.Territory == route.TerritoryId && c.Map == route.MapId) EnterMap(c, now);
                break;
            case PatrolState.PreparingMove:
                if (!_host.PrepareMount(_options.Mount, _options.Takeoff, out var prepare)) { Message = prepare; return; }
                if (!_host.Move(route, route.Points[PointIndex], out _target, out var moveError))
                { Stop($"扫描点 {PointIndex + 1} 无法寻路：{moveError}"); return; }
                _ownsNavigation = true; State = PatrolState.Moving; _deadline = now + 180000;
                Message = $"{route.Name}：前往扫描点 {PointIndex + 1}/{route.Points.Count}";
                break;
            case PatrolState.Moving:
                if (Vector3.Distance(c.Position, _target) <= _options.Radius)
                {
                    _host.StopNavigation(); _ownsNavigation = false;
                    State = PatrolState.Scanning; _deadline = now + _options.WaitMs + 10000;
                    _scanUntil = now + _options.WaitMs;
                    Message = $"{route.Name}：扫描点 {PointIndex + 1}/{route.Points.Count}，等待怪物加载";
                }
                else if (!_host.Navigating) Stop($"未到达扫描点：{_host.NavigationMessage}");
                break;
            case PatrolState.Scanning:
                if (now < _scanUntil) break;
                PointIndex++;
                if (PointIndex >= route.Points.Count) NextMap(now);
                else { State = PatrolState.PreparingMove; _deadline = now + 120000; }
                break;
        }
    }

    private long _scanUntil;
    private void EnterMap(PatrolContext c, long now)
    { _instance = c.Instance; State = PatrolState.PreparingMove; _deadline = now + 120000; }
    private void NextMap(long now)
    {
        if (_ownsNavigation) _host.StopNavigation();
        _ownsNavigation = false; _route++; PointIndex = 0; _foundA.Clear(); _foundB.Clear();
        if (_route >= _routes.Count) { State = PatrolState.Completed; Message = "所选地图巡点完成，结果已按当前服务器保存"; }
        else { State = PatrolState.PreparingMap; _deadline = now + 120000; }
    }
}
