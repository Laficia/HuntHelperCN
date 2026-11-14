using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

// One map/instance only. The parent session owns world travel, recording and
// completion by species count. A covered location never creates a monster record.
public sealed class DRCoveragePatrol
{
    private readonly IPatrolHost _host;
    private readonly PatrolRoute _route;
    private readonly PatrolOptions _options;
    private readonly Vector3[] _points;
    private readonly HashSet<int> _pending;
    private readonly HashSet<int> _passed = new();
    private readonly HashSet<int> _targets = new();
    private readonly List<Vector3> _failed = new();
    private Queue<DRCoveragePlan>? _plans;
    private Vector3 _stable, _previous, _destination;
    private long _started, _lastProgress, _lastTeleport;
    private float _closest;
    private bool _ownsPath, _busySeen;
    private PatrolTeleport? _teleport;

    public PatrolState State { get; private set; } = PatrolState.Scanning;
    public string Message { get; private set; } = "DR 覆盖扫描：原地等待怪物加载";
    public bool Finished { get; private set; }
    public bool Failed { get; private set; }
    public int CoveredCount => _points.Length - _pending.Count;
    public int Anchor { get; private set; }
    public uint Instance { get; }
    private readonly uint _world;
    public Vector3? Target => State == PatrolState.Moving && _ownsPath ? _destination : null;

    public IReadOnlyList<DRPreviewPoint> GetPreviewPoints() => _points
        .Select((point, index) => new DRPreviewPoint(point, !_pending.Contains(index), _passed.Contains(index))).ToArray();

    public DRCoveragePatrol(IPatrolHost host, PatrolRoute route, PatrolOptions options, PatrolContext context, long now)
    {
        _host = host; _route = route; _options = options;
        _points = route.Points.Select(p => p.Position!.Value).ToArray();
        _pending = Enumerable.Range(0, _points.Length).ToHashSet();
        _stable = context.Position; _started = now; Instance = context.Instance; _world = context.World;
        _lastTeleport = now - 60000;
        if (CanPreview(context)) MarkPassed(context.Position, context.Position);
    }

    public void Stop()
    {
        if (_ownsPath) _host.StopNavigation();
        _ownsPath = false;
    }

    public void Tick(PatrolContext context, long now)
    {
        if (Finished || Failed) return;
        if (now - _started > 180000) { Fail("DR 覆盖扫描步骤超时"); return; }
        if (State == PatrolState.Teleporting)
        {
            _busySeen |= !context.Ready;
            if (!context.Ready) return;
            if (context.Territory != _route.TerritoryId || context.Map != _route.MapId || context.Instance != Instance)
            { Fail("水晶传送后地图或分线改变，已停止覆盖扫描"); return; }
            if (!_busySeen || _teleport?.WorldPosition is not { } crystal ||
                Vector2.Distance(PatrolTravelPlanner.Horizontal(context.Position), crystal) > 100) return;
            Observe(context.Position, now);
            if (CanPreview(context)) MarkPassed(context.Position, context.Position);
            return;
        }
        if (!context.Ready) return;
        if (_ownsPath && _host.NavigationInterrupted)
        { Fail("vnavmesh 已中止移动，覆盖扫描已停止"); return; }
        if (CanPreview(context)) MarkPassed(context.Position, context.Position);

        switch (State)
        {
            case PatrolState.Scanning:
                if (Vector3.DistanceSquared(context.Position, _stable) > 1)
                { _stable = context.Position; _started = now; return; }
                if (now - _started < DRCoveragePlanner.ObservationMs) return;
                Cover(context.Position, context.Position, true);
                if (_pending.Count == 0) { Finished = true; State = PatrolState.Completed; Message = "DR 点位覆盖完成"; return; }
                _plans = null; State = PatrolState.PreparingMove; _started = now;
                Message = $"DR 覆盖扫描：已覆盖 {CoveredCount}/{_points.Length}，正在选择观察点";
                break;
            case PatrolState.PreparingMove:
                Select(context, now);
                break;
            case PatrolState.Moving:
                Move(context, now);
                break;
        }
    }

    private void Select(PatrolContext context, long now)
    {
        if (!_host.PrepareMount(true, true, out var prepare)) { Message = prepare; return; }
        if (_plans == null)
        {
            var crystals = _options.SmartTeleport && now - _lastTeleport >= 60000
                ? _host.CoverageCrystals(_route) : Array.Empty<PatrolCoverageCrystal>();
            _plans = new(DRCoveragePlanner.Plan(context.Position, _points, _pending, crystals,
                12, _options.TeleportSeconds));
        }
        // Bound IPC lookups per framework tick; do not stall the game's UI on a large map.
        for (var attempt = 0; attempt < 16 && _plans.TryDequeue(out var plan); attempt++)
        {
            if (_failed.Any(p => Vector3.DistanceSquared(p, plan.Destination) < 1) ||
                !_host.TryCoverageDestination(plan.Destination, out var destination) ||
                !Utilities.NavigationRules.IsFinite(destination) ||
                DRCoveragePlanner.DistanceSquared(destination, _points[plan.Anchor]) > DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius + .1f)
                continue;
            Anchor = plan.Anchor;
            if (plan.Crystal is { } crystal)
            {
                var direct = MathF.Sqrt(DRCoveragePlanner.DistanceSquared(context.Position, destination)) / 12;
                _teleport = new(crystal.Crystal, crystal.Position, direct, plan.Seconds);
                if (!_host.Teleport(_route, _teleport, out var error)) { Fail(error); return; }
                _lastTeleport = now; _busySeen = false; _started = now; State = PatrolState.Teleporting;
                Message = $"DR 覆盖扫描：正常传送至{crystal.Crystal.Name}，确认到达后重新规划";
                return;
            }
            _targets.Clear();
            _targets.UnionWith(_pending.Where(i => DRCoveragePlanner.DistanceSquared(_points[i], destination) <=
                DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius + .1f));
            var point = new PatrolPoint { Position = destination, ForA = true, ForB = false, IsObservationPoint = true };
            if (!_host.Move(_route, point, out _destination, out _))
            { _failed.Add(plan.Destination); continue; }
            _ownsPath = true; _previous = context.Position;
            _closest = DRCoveragePlanner.DistanceSquared(context.Position, _destination);
            _lastProgress = _started = now; State = PatrolState.Moving;
            Message = $"DR 覆盖扫描：前往观察点，已覆盖 {CoveredCount}/{_points.Length}";
            return;
        }
        if (_plans.Count == 0) Fail("DR 剩余刷新点没有可用飞行路线，请检查飞行解锁及 vnavmesh 网格");
    }

    private void Move(PatrolContext context, long now)
    {
        if (CanPreview(context)) MarkPassed(_previous, context.Position);
        Cover(_previous, context.Position, false);
        _previous = context.Position;
        var distance = DRCoveragePlanner.DistanceSquared(context.Position, _destination);
        if (distance <= DRCoveragePlanner.ArrivalRadius * DRCoveragePlanner.ArrivalRadius ||
            _targets.All(i => DRCoveragePlanner.DistanceSquared(context.Position, _points[i]) <=
                DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius))
        { Stop(); Observe(context.Position, now); return; }
        // A native stop is final, even when a retry could otherwise reach the target.
        if (!_host.Navigating) { Fail("vnavmesh 已停止移动，覆盖扫描已停止"); return; }
        if (distance < _closest - 4) { _closest = distance; _lastProgress = now; }
        if (now - _lastProgress > 15000)
        {
            Stop();
            Fail("覆盖扫描移动超过15秒没有进展，请检查路线后重新开始");
        }
    }

    private void Observe(Vector3 position, long now)
    {
        State = PatrolState.Scanning; _stable = position; _started = now;
        Message = $"DR 覆盖扫描：等待怪物加载，已覆盖 {CoveredCount}/{_points.Length}";
    }

    private void Cover(Vector3 a, Vector3 b, bool observing)
    {
        _pending.RemoveWhere(i => (observing || !_targets.Contains(i)) &&
            DRCoveragePlanner.SegmentDistanceSquared(_points[i], a, b) <= DRCoveragePlanner.DetectionRadius * DRCoveragePlanner.DetectionRadius);
    }

    private bool CanPreview(PatrolContext context) => context.LoggedIn && context.Ready && context.World != 0 && context.World == _world &&
        context.Territory == _route.TerritoryId && context.Map == _route.MapId && context.Instance == Instance &&
        !context.Dead && !context.InCombat && Utilities.NavigationRules.IsFinite(context.Position);

    private void MarkPassed(Vector3 start, Vector3 end)
    {
        for (var index = 0; index < _points.Length; index++)
            if (DRCoveragePlanner.SegmentDistanceSquared(_points[index], start, end) <=
                DRCoveragePlanner.DetectionRadius * DRCoveragePlanner.DetectionRadius)
                _passed.Add(index);
    }

    private void Fail(string message)
    { Stop(); Failed = true; State = PatrolState.Stopped; Message = message; }
}
