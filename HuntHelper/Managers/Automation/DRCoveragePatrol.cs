using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

// One map/instance only. The parent session owns world travel, recording and
// completion by species count. A covered location never creates a monster record.
public sealed class DRCoveragePatrol
{
    private const int CandidatesPerFrame = 8;
    private const int PlanningBudgetMs = 3;
    private readonly IPatrolHost _host;
    private readonly PatrolRoute _route;
    private readonly Vector3[] _points;
    private readonly HashSet<int> _pending;
    private readonly HashSet<int> _passed = new();
    private readonly HashSet<int> _targets = new();
    private readonly List<Vector3> _failed = new();
    private IEnumerator<DRCoveragePlan?>? _planning;
    private Vector3 _planningOrigin;
    private bool _planningCompleted;
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
    private enum FerryStage { None, ObserveDeparture, Sweep, ObserveSweep, Approach, Arrival }
    private enum RubySeaPriority { None, South, Northeast }
    private FerryStage _ferry;
    private long _ferryDeadline;
    private Vector3 _rubySweepDestination;
    private bool _ferryLandingRequested;
    private RubySeaPriority _rubyPriority;
    private IReadOnlyCollection<int>? _planningPoints;

    public PatrolState State { get; private set; } = PatrolState.Scanning;
    public string Message { get; private set; } = "DR 覆盖扫描：原地等待怪物加载";
    public bool Finished { get; private set; }
    public bool Failed { get; private set; }
    public int CoveredCount => _points.Length - _pending.Count;
    public int Anchor { get; private set; }
    public uint Instance { get; }
    private readonly uint _world;
    public Vector3? Target => State == PatrolState.Moving && _ownsPath ? _destination : null;
    internal Vector3? SpawnTarget => _planningCompleted && _best is { } plan &&
        State is PatrolState.PreparingMove or PatrolState.Moving ? _points[plan.Anchor] : null;
    internal bool IsPlanning => _planning != null;

    internal void AdvancePlanning(PatrolContext context)
    {
        if (_planning == null || !context.Ready || context.World != _world || context.Territory != _route.TerritoryId ||
            context.Map != _route.MapId || context.Instance != Instance || context.Dead || !_host.CoverageNavigationReady)
            return;
        if (Vector3.DistanceSquared(context.Position, _planningOrigin) > 4)
        {
            ResetPlanning();
            return;
        }

        // Retain candidate order and compare the whole set before using the winner.
        // Native voxel queries run on the game thread, so yield between batches.
        var started = Stopwatch.GetTimestamp();
        for (var checkedCount = 0; checkedCount < CandidatesPerFrame; checkedCount++)
        {
            if (!_planning.MoveNext())
            {
                _planning.Dispose();
                _planning = null;
                _planningCompleted = true;
                return;
            }
            if (_planning.Current is { } scored &&
                (_best == null || scored.Score < _best.Score || scored.Score == _best.Score && scored.Coverage > _best.Coverage))
                _best = scored;
            if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= PlanningBudgetMs) return;
        }
    }

    public IReadOnlyList<DRPreviewPoint> GetPreviewPoints() => _points
        .Select((point, index) => new DRPreviewPoint(point, !_pending.Contains(index), _passed.Contains(index))).ToArray();

    public DRCoveragePatrol(IPatrolHost host, PatrolRoute route, PatrolOptions options, PatrolContext context, long now)
    {
        _host = host; _route = route;
        _points = route.Points.Select(p => p.Position!.Value).ToArray();
        _pending = Enumerable.Range(0, _points.Length).ToHashSet();
        _stable = context.Position; _started = now; Instance = context.Instance; _world = context.World;
        if (CanPreview(context)) MarkPassed(context.Position, context.Position);
        if (route.TerritoryId == DRScanRules.RubySea && _points.Count(DRScanRules.IsRubySeaSouthPoint) == 3)
        {
            if (AtRubySeaLanding(context.Position)) _rubyPriority = RubySeaPriority.South;
            else if (Vector3.DistanceSquared(context.Position, DRScanRules.RubySeaFerryDock) <= 100 * 100)
            {
                _ferry = FerryStage.ObserveDeparture;
                _ferryDeadline = now + 25000;
                Message = "自凝岛：扫描水晶周边";
            }
        }
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
        if (now - _started > 180000) { Fail($"{_route.Name}：{Message}，等待超时"); return; }
        if (_ferry != FerryStage.None) { TickFerry(context, now); return; }
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
        if (!_host.CoverageNavigationReady)
        {
            Message = "DR 覆盖扫描：等待 vnavmesh 飞行网格就绪";
            return;
        }
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

    private static bool AtRubySeaLanding(Vector3 position) =>
        Vector3.DistanceSquared(position, DRScanRules.RubySeaFerryLanding) <= 40 * 40;

    private void TickFerry(PatrolContext context, long now)
    {
        if (!context.Ready) return;
        if (_ferry == FerryStage.Arrival && AtRubySeaLanding(context.Position))
        {
            _ferry = FerryStage.None;
            _rubyPriority = RubySeaPriority.South;
            Observe(context.Position, now);
            Message = "已乘船抵达，准备巡查南侧三个 A 点";
            return;
        }
        if (_ferry is FerryStage.ObserveDeparture or FerryStage.Sweep or FerryStage.ObserveSweep)
        { TickRubySeaDeparture(context, now); return; }
        if (now >= _ferryDeadline || context.InCombat)
        { FallBackFromFerry(context.Position, now); return; }
        if (_ferry == FerryStage.Arrival) return;

        var ferry = _host.FindRubySeaFerry();
        if (ferry is { } dock && Vector3.DistanceSquared(context.Position, dock) <=
            DRScanRules.RubySeaFerryInteractionRange * DRScanRules.RubySeaFerryInteractionRange)
        {
            if (!_host.CanBoardRubySeaFerry)
            {
                PrepareFerryBoarding(context.Position, dock);
                return;
            }
            Stop();
            if (!_host.BoardRubySeaFerry()) { FallBackFromFerry(context.Position, now); return; }
            _ferry = FerryStage.Arrival;
            _ferryDeadline = now + 18000;
            State = PatrolState.Teleporting;
            Message = "已与船头交互，等待抵达红玉海南岸";
            return;
        }
        if (_ownsPath)
        {
            if (!_host.Navigating && !_host.RubySeaFerryAirborne) FallBackFromFerry(context.Position, now);
            return;
        }
        if (!_host.CoverageNavigationReady || !_host.PrepareMount(true, false, out _)) return;
        var approach = ferry ?? DRScanRules.RubySeaFerryDock;
        if (!_host.ApproachRubySeaFerry(_route, approach)) { FallBackFromFerry(context.Position, now); return; }
        _ownsPath = true;
        _destination = approach;
        State = PatrolState.Moving;
    }

    private void PrepareFerryBoarding(Vector3 position, Vector3 dock)
    {
        if (!_host.RubySeaFerryAirborne)
        {
            Stop();
            _host.DismountAtRubySeaFerry();
            State = PatrolState.Scanning;
            Message = "船头：下坐骑后乘船";
            return;
        }
        if (_host.Navigating || _ferryLandingRequested || MathF.Abs(position.Y - dock.Y) > 1.2f ||
            DRCoveragePlanner.DistanceSquared(position, dock) > 1.5f * 1.5f) return;
        Stop();
        _ferryLandingRequested = true;
        _destination = dock;
        _ownsPath = _host.LandAtRubySeaFerry(_route, dock);
        Message = "船头：正在落地";
    }

    private void TickRubySeaDeparture(PatrolContext context, long now)
    {
        if (context.InCombat) { FallBackFromFerry(context.Position, now); return; }
        if (now >= _ferryDeadline) { BeginFerryApproach(context.Position, now); return; }
        if (_ferry is FerryStage.ObserveDeparture or FerryStage.ObserveSweep)
        {
            if (DRCoveragePlanner.DistanceSquared(context.Position, _stable) > 1)
            { _stable = context.Position; _started = now; return; }
            if (now - _started < DRCoveragePlanner.ObservationMs) return;
            Cover(context.Position, context.Position, true);
            if (_ferry == FerryStage.ObserveDeparture && TryRubySeaSweepDestination(context.Position, out _rubySweepDestination))
            {
                _ferry = FerryStage.Sweep;
                _ferryDeadline = now + 20000;
                State = PatrolState.PreparingMove;
                Message = "自凝岛：向西南短距离补扫";
                return;
            }
            BeginFerryApproach(context.Position, now);
            return;
        }

        if (!_ownsPath)
        {
            if (!_host.CoverageNavigationReady || !_host.PrepareMount(true, true, out _)) return;
            if (!_host.StartRubySeaSweep(_route, _rubySweepDestination))
            { BeginFerryApproach(context.Position, now); return; }
            _ownsPath = true;
            _destination = _rubySweepDestination;
            State = PatrolState.Moving;
            return;
        }
        if (DRCoveragePlanner.DistanceSquared(context.Position, DRScanRules.RubySeaUnderwaterSpawn) <= 390 * 390)
        {
            Stop();
            _ferry = FerryStage.ObserveSweep;
            _stable = context.Position;
            _started = now;
            State = PatrolState.Scanning;
            Message = "自凝岛：停留补扫第三个点";
        }
        else if (!_host.Navigating) BeginFerryApproach(context.Position, now);
    }

    private static bool TryRubySeaSweepDestination(Vector3 origin, out Vector3 destination)
    {
        destination = default;
        if (Vector3.DistanceSquared(origin, DRScanRules.RubySeaCrystal) > 25 * 25) return false;
        var delta = DRScanRules.RubySeaUnderwaterSpawn - origin;
        delta.Y = 0;
        var distance = delta.Length();
        if (distance <= DRCoveragePlanner.DetectionRadius) return false;
        destination = origin + delta * ((distance - 380) / distance) + new Vector3(0, 12, 0);
        return true;
    }

    private void BeginFerryApproach(Vector3 position, long now)
    {
        Stop();
        Observe(position, now);
        _ferry = FerryStage.Approach;
        _ferryDeadline = now + 25000;
        Message = "前往自凝岛船头";
    }

    private void FallBackFromFerry(Vector3 position, long now)
    {
        Stop();
        _ferry = FerryStage.None;
        Observe(position, now);
        Message = "船头捷径未完成，继续 DR 覆盖扫描";
    }

    private IReadOnlyCollection<int> GetPlanningPoints()
    {
        if (_rubyPriority == RubySeaPriority.South)
        {
            var south = _pending.Where(i => DRScanRules.IsRubySeaSouthPoint(_points[i])).ToArray();
            if (south.Length > 0) return south;
            _rubyPriority = RubySeaPriority.Northeast;
        }
        if (_rubyPriority == RubySeaPriority.Northeast)
        {
            var northeast = _pending.Where(i => DRScanRules.IsRubySeaEastPoint(_points[i])).ToArray();
            if (northeast.Length > 0) return northeast;
            _rubyPriority = RubySeaPriority.None;
        }
        return _pending;
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
        if (_planning == null && !_planningCompleted)
        {
            _planningPoints = GetPlanningPoints();
            if (_planningPoints.All(i => DRCoveragePlanner.DistanceSquared(context.Position, _points[i]) <=
                DRCoveragePlanner.PlanningRadius * DRCoveragePlanner.PlanningRadius))
            { Observe(context.Position, now); return; }
            _planningOrigin = context.Position;
            var crystals = _rubyPriority == RubySeaPriority.None ? _host.CoverageCrystals(_route) : Array.Empty<PatrolCoverageCrystal>();
            _planning = DRCoveragePlanner.Plan(_planningOrigin, _points, _planningPoints, crystals,
                candidate => _host.TryCoverageDestination(candidate, out var point) &&
                    !_failed.Any(f => Vector3.DistanceSquared(f, point) < 1) ? point : null).GetEnumerator();
        }
        AdvancePlanning(context);
        if (!_planningCompleted) return;
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
        // A busy or paused executor is temporary. Do not dequeue and mark every
        // geometric candidate unreachable while another navigation request owns it.
        if (_host.CoveragePathStartProblem is { Length: > 0 } problem)
        {
            Message = $"DR 覆盖扫描：{problem}";
            return;
        }
        if (_retries == null)
        {
            _targets.Clear();
            _targets.UnionWith(_planningPoints!.Where(i => DRCoveragePlanner.DistanceSquared(_points[i], plan.Destination) <=
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
    {
        _planning?.Dispose();
        _planning = null; _planningCompleted = false; _best = null;
        _planningPoints = null;
        _retries = null; _mountStarted = null; _mountPrepared = false;
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
