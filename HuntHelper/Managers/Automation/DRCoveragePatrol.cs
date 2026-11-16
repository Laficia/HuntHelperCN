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
    private readonly Vector3[] _points;
    private readonly HashSet<int> _pending;
    private readonly HashSet<int> _passed = new();
    private readonly HashSet<int> _targets = new();
    private readonly List<Vector3> _failed = new();
    private IEnumerator<DRCoveragePlan?>? _planning;
    private DRCoveragePlan? _best;
    private Queue<Vector3>? _retries;
    private Vector3 _stable, _previous, _destination;
    private long _started, _lastProgress;
    private float _closest;
    private bool _ownsPath, _busySeen;
    private bool _pathPending;
    private long _pathStarted;
    private long? _mountStarted;
    private bool _mountPrepared;
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
        _host = host; _route = route;
        _points = route.Points.Select(p => p.Position!.Value).ToArray();
        _pending = Enumerable.Range(0, _points.Length).ToHashSet();
        _stable = context.Position; _started = now; Instance = context.Instance; _world = context.World;
        if (CanPreview(context)) MarkPassed(context.Position, context.Position);
    }

    public void Stop()
    {
        if (_ownsPath) _host.StopNavigation();
        _ownsPath = false;
        _pathPending = false;
        ResetPlanning();
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
        if (!_host.CoverageNavigationReady) return;
        if (CanPreview(context)) MarkPassed(context.Position, context.Position);

        switch (State)
        {
            case PatrolState.Scanning:
                if (DRCoveragePlanner.DistanceSquared(context.Position, _stable) > 1)
                { _stable = context.Position; _started = now; return; }
                if (now - _started < DRCoveragePlanner.ObservationMs) return;
                Cover(context.Position, context.Position, true);
                if (_pending.Count == 0) { Finished = true; State = PatrolState.Completed; Message = "DR 点位覆盖完成"; return; }
                ResetPlanning(); State = PatrolState.PreparingMove; _started = now;
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
        if (_pathPending)
        {
            if (_host.CoveragePathPending && now - _pathStarted < 15000) return;
            var path = _host.TakeCoveragePath();
            _host.StopNavigation(); _ownsPath = _pathPending = false;
            var end = path?.FindIndex(p => DRCoveragePlanner.DistanceSquared(p, _points[Anchor]) <=
                DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius) ?? -1;
            if (end < 0) { _failed.Add(_destination); return; }
            path!.RemoveRange(end + 1, path.Count - end - 1);
            _destination = path[^1];
            if (!_host.FollowCoveragePath(_route, path)) { _failed.Add(_destination); return; }
            _ownsPath = true; _previous = context.Position;
            _closest = DRCoveragePlanner.DistanceSquared(context.Position, _destination);
            _lastProgress = _started = now; State = PatrolState.Moving;
            Message = $"DR 覆盖扫描：前往观察点，已覆盖 {CoveredCount}/{_points.Length}";
            return;
        }
        if (_planning == null && _best == null)
        {
            if (_pending.All(i => DRCoveragePlanner.DistanceSquared(context.Position, _points[i]) <=
                DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius))
            { Observe(context.Position, now); return; }
            _planning = DRCoveragePlanner.Plan(context.Position, _points, _pending, _host.CoverageCrystals(_route),
                candidate => _host.TryCoverageDestination(candidate, out var point) &&
                    !_failed.Any(f => Vector3.DistanceSquared(f, point) < 1) ? point : null).GetEnumerator();
        }
        // DR compares the full candidate set in one update, preserving input
        // order and the first candidate when both score and coverage are tied.
        while (_planning != null)
        {
            if (!_planning.MoveNext()) { _planning.Dispose(); _planning = null; break; }
            if (_planning.Current is not { } scored) continue;
            if (_best == null || scored.Score < _best.Score || scored.Score == _best.Score && scored.Coverage > _best.Coverage)
                _best = scored;
        }
        if (_planning != null) return;
        if (_best is not { } plan) { Fail("DR 剩余刷新点没有可用飞行路线，请检查飞行解锁及 vnavmesh 网格"); return; }
        Anchor = plan.Anchor;
        if (plan.Crystal is { } crystal)
        {
            var direct = MathF.Sqrt(DRCoveragePlanner.DistanceSquared(context.Position, plan.Destination)) / 12;
            _teleport = new(crystal.Crystal, crystal.Position, direct, plan.Seconds);
            if (!_host.Teleport(_route, _teleport, out var error)) { Fail(error); return; }
            _busySeen = false; _started = now; State = PatrolState.Teleporting;
            Message = $"DR 覆盖扫描：正常传送至{crystal.Crystal.Name}，确认到达后重新规划";
            return;
        }
        if (!_mountPrepared)
        {
            _mountStarted ??= now;
            if (_host.CoverageMountError is { Length: > 0 } mountError) { Fail(mountError); return; }
            if (!_host.PrepareMount(true, true, out var prepare))
            {
                if (now - _mountStarted >= 20000) { Fail("DR 坐骑准备超时"); return; }
                Message = prepare; return;
            }
            _mountPrepared = true;
        }
        if (_retries == null)
        {
            _targets.Clear();
            _targets.UnionWith(_pending.Where(i => DRCoveragePlanner.DistanceSquared(_points[i], plan.Destination) <=
                DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius));
            _retries = new(DRCoveragePlanner.RetryTargets(plan.Destination, _points[Anchor]));
        }
        while (_retries.TryDequeue(out var candidate))
        {
            if (!_host.TryCoverageDestination(candidate, out var destination) || !Utilities.NavigationRules.IsFinite(destination) ||
                _failed.Any(p => Vector3.DistanceSquared(p, destination) < 1) ||
                DRCoveragePlanner.DistanceSquared(destination, _points[Anchor]) > DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius)
                continue;
            if (!_host.BeginCoveragePath(_route, destination)) { _failed.Add(destination); continue; }
            _destination = destination; _pathPending = _ownsPath = true; _pathStarted = _started = now;
            Message = "DR 覆盖扫描：正在计算飞行路线";
            return;
        }
        if (_retries.Count == 0) { _failed.Add(plan.Destination); ResetPlanning(); }
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
        if (distance < _closest - 4) { _closest = distance; _lastProgress = now; }
        if (!_host.Navigating || now - _lastProgress > 15000)
        {
            _host.StopNavigation(); _ownsPath = false;
            if (DRCoveragePlanner.DistanceSquared(context.Position, _points[Anchor]) <=
                DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius)
                Observe(context.Position, now);
            else
            {
                _failed.Add(_destination); State = PatrolState.PreparingMove; _started = now;
                Message = "DR 覆盖扫描：重新尝试飞行路线";
            }
        }
    }

    private void Observe(Vector3 position, long now)
    {
        ResetPlanning();
        State = PatrolState.Scanning; _stable = position; _started = now;
        Message = $"DR 覆盖扫描：等待怪物加载，已覆盖 {CoveredCount}/{_points.Length}";
    }

    private void ResetPlanning()
    { _planning?.Dispose(); _planning = null; _best = null; _retries = null; _mountStarted = null; _mountPrepared = false; }

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
