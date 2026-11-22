using System.Numerics;
using HuntHelper.Managers.Automation;

internal static class DRPathPatrolChecks
{
    internal static readonly (string Name, Action Run)[] Tests = [
        ("DR waits for the asynchronous path and truncates at the first node within 300m", TruncatedPath),
        ("DR retries a cancelled route outside observation range", CancelRetry),
        ("DR observes after native cancellation inside the anchor range", CancelObserve),
        ("DR retries null paths paths missing the anchor and 15-second calculations", InvalidPathRetry),
        ("DR retries after 15 seconds without progress", StalledRetry),
        ("HHT stop during DR calculation or movement prevents every later restart", FinalStop),
        ("DR pending calculations stop on world changes without recording another server", WorldGuard),
        ("DR keeps the original combat behavior and stops on death", CombatAndDeath),
        ("DR mount timeout is 20 seconds and unavailable flight fails immediately", MountRules),
        ("DR observation stability ignores vertical movement", HorizontalObservation),
        ("DR waits for the native navigation grid without marking points or dispatching a path", GridWait),
        ("DR spreads dense voxel lookups across frames and still chooses the highest-ranked destination", PlanningBatches),
    ];
    private static void Check(bool value) { if (!value) throw new Exception("DR path patrol assertion failed"); }
    private static PatrolRoute Route(float x = 700) => new() { TerritoryId = 10, MapId = 20,
        Points = [new() { Position = new(x, 0, 0), ForA = true, ForB = false }] };
    private static PatrolOptions Options() => new() { RouteMode = PatrolRouteMode.DRCoverage, UseRecordedHunts = false };
    private static (Host Host, PatrolSession Session, long Now) Start()
    {
        var host = new Host(); var session = new PatrolSession(host);
        Check(session.Start([Route()], Options(), 0));
        return (host, session, Until(session, () => host.Requests > 0, 0));
    }
    private static long Until(PatrolSession session, Func<bool> done, long now)
    {
        for (var i = 0; i < 200 && !done(); i++) { now += 100; session.Tick(now); }
        Check(done()); return now;
    }
    private static void TruncatedPath()
    {
        var (h, s, t) = Start();
        s.Tick(t + 100); Check(h.Follows == 0 && s.Active && h.LegacyMoves == 0);
        h.CoveragePathPending = false; s.Tick(t + 200);
        Check(h.Follows == 1 && h.Followed.SequenceEqual(new Vector3[] { new(50, 7, 0), new(400, 9, 0) }));
        Check(s.State == PatrolState.Moving && h.LegacyMoves == 0); s.Stop();
    }
    private static void CancelRetry()
    {
        var (h, s, t) = Start(); h.CoveragePathPending = false; s.Tick(t += 100);
        h.Navigating = false; h.NavigationInterrupted = true; s.Tick(t += 100);
        Check(s.Active && s.State == PatrolState.PreparingMove);
        Until(s, () => h.Requests == 2, t); Check(h.Follows == 1); s.Stop();
    }
    private static void CancelObserve()
    {
        var (h, s, t) = Start(); h.CoveragePathPending = false; s.Tick(t += 100);
        h.Context = h.Context with { Position = new(400, 0, 0) };
        h.Navigating = false; h.NavigationInterrupted = true; s.Tick(t += 100);
        Check(s.Active && s.State == PatrolState.Scanning && h.Requests == 1); s.Stop();
    }
    private static void InvalidPathRetry()
    {
        foreach (var mode in new[] { 0, 1, 2 })
        {
            var (h, s, t) = Start();
            h.Result = mode == 1 ? [new(50, 0, 0)] : null;
            if (mode != 2) h.CoveragePathPending = false;
            s.Tick(t += mode == 2 ? 15000 : 100);
            Until(s, () => h.Requests == 2, t);
            Check(s.Active && h.Follows == 0 && h.Stops > 0); s.Stop();
        }
    }
    private static void StalledRetry()
    {
        var (h, s, t) = Start(); h.CoveragePathPending = false; s.Tick(t += 100);
        s.Tick(t += 15001); Check(s.Active && s.State == PatrolState.PreparingMove);
        Until(s, () => h.Requests == 2, t); s.Stop();
    }
    private static void FinalStop()
    {
        foreach (var moving in new[] { false, true })
        {
            var (h, s, t) = Start();
            if (moving) { h.CoveragePathPending = false; s.Tick(t += 100); }
            s.Stop(); var requests = h.Requests; var follows = h.Follows;
            h.CoveragePathPending = false;
            for (var i = 0; i < 30; i++) s.Tick(t += 1000);
            Check(!s.Active && h.Requests == requests && h.Follows == follows && h.Ends == 1 && !h.Navigating);
        }
    }
    private static void WorldGuard()
    {
        var (h, s, t) = Start(); h.Context = h.Context with { World = 102 };
        s.Tick(t + 100); h.CoveragePathPending = false; s.Tick(t + 1000);
        Check(!s.Active && h.Follows == 0 && h.Ends == 1 && h.ScannedWorlds.All(w => w == 101));
    }
    private static void CombatAndDeath()
    {
        var (h, s, t) = Start(); h.Context = h.Context with { InCombat = true };
        s.Tick(t + 100); Check(s.Active && h.Requests == 1);
        h.Context = h.Context with { Dead = true }; s.Tick(t + 200);
        Check(!s.Active && h.Ends == 1);
    }
    private static void MountRules()
    {
        foreach (var unavailable in new[] { false, true })
        {
            var h = new Host { MountReady = false, CoverageMountError = unavailable ? "没有飞行" : "" };
            var s = new PatrolSession(h); Check(s.Start([Route()], Options(), 0));
            var t = Until(s, () => h.MountRequests > 0 || !s.Active, 0);
            if (!unavailable) { s.Tick(t + 19999); Check(s.Active); s.Tick(t + 20000); }
            Check(!s.Active && h.Requests == 0 && h.Ends == 1);
        }
    }
    private static void HorizontalObservation()
    {
        var h = new Host(); var patrol = new DRCoveragePatrol(h, Route(100), Options(), h.Context, 0);
        patrol.Tick(h.Context with { Position = new(0, 20, 0) }, 250);
        Check(patrol.Finished && h.Requests == 0);
    }
    private static void GridWait()
    {
        var h = new Host { CoverageNavigationReady = false };
        var patrol = new DRCoveragePatrol(h, Route(100), Options(), h.Context, 0);
        patrol.Tick(h.Context, 250); patrol.Tick(h.Context, 1000);
        Check(!patrol.Finished && patrol.CoveredCount == 0 && h.Requests == 0);
        h.CoverageNavigationReady = true; patrol.Tick(h.Context, 1250); Check(patrol.Finished);
    }

    private static void PlanningBatches()
    {
        var host = new Host();
        var route = Route();
        var points = Enumerable.Range(0, 12).Select(i => new Vector3(700 + i * 18, 0, i * 12)).ToArray();
        route.Points = points.Select(point => new PatrolPoint { Position = point, ForA = true }).ToList();
        var best = DRCoveragePlanner.Plan(host.Context.Position, points, Enumerable.Range(0, points.Length).ToArray(), [], p => p)
            .OfType<DRCoveragePlan>().OrderBy(p => p.Score).ThenByDescending(p => p.Coverage).First();
        var patrol = new DRCoveragePatrol(host, route, Options(), host.Context, 0);
        patrol.Tick(host.Context, 250);
        patrol.Tick(host.Context, 500);
        Check(patrol.IsPlanning && host.CoverageResolutions is > 0 and <= 8 && host.Requests == 0);
        for (var frame = 0; patrol.IsPlanning && frame < 100; frame++)
        {
            var before = host.CoverageResolutions;
            patrol.AdvancePlanning(host.Context);
            Check(host.CoverageResolutions - before <= 8 && host.Requests == 0);
        }
        Check(!patrol.IsPlanning);
        patrol.Tick(host.Context, 750);
        Check(host.Requests == 1 && host.RequestedDestination == best.Destination);
        patrol.Stop();
    }
    private sealed class Host : IPatrolHost
    {
        public PatrolContext Context { get; set; } = new(101, 10, 20, 1, Vector3.Zero, true, true, false, false);
        public bool CoveragePathPending { get; set; }
        public bool CoverageNavigationReady { get; set; } = true;
        public bool Navigating { get; set; }
        public bool NavigationInterrupted { get; set; }
        public string NavigationMessage => "";
        public string CoverageMountError { get; set; } = "";
        public bool MountReady = true;
        public int Requests, Follows, Stops, Ends, LegacyMoves, MountRequests;
        public int CoverageResolutions;
        public Vector3 RequestedDestination;
        public List<Vector3>? Result = [new(50, 7, 0), new(400, 9, 0), new(650, 11, 0), new(700, 12, 0)];
        public List<Vector3> Followed = [];
        public List<uint> ScannedWorlds = [];
        public bool BeginCoveragePath(PatrolRoute route, Vector3 destination)
        { Requests++; RequestedDestination = destination; CoveragePathPending = true; NavigationInterrupted = false; return true; }
        public bool TryCoverageDestination(Vector3 candidate, out Vector3 destination)
        { CoverageResolutions++; destination = candidate; return true; }
        public List<Vector3>? TakeCoveragePath() => CoveragePathPending ? null : Result?.ToList();
        public bool FollowCoveragePath(PatrolRoute route, List<Vector3> path)
        { Follows++; Followed = path.ToList(); Navigating = true; return true; }
        public void EndCoverage() => Ends++;
        public bool PrepareMount(bool mount, bool takeoff, out string message)
        { MountRequests++; message = "等待坐骑"; return MountReady; }
        public void StopNavigation() { Stops++; CoveragePathPending = false; Navigating = false; }
        public bool Teleport(PatrolRoute route, out string error) { error = ""; return false; }
        public bool Move(PatrolRoute route, PatrolPoint point, out Vector3 destination, out string error)
        { LegacyMoves++; destination = default; error = ""; return false; }
        public IReadOnlyList<PatrolDetection> Scan(bool rankA, bool rankB) { ScannedWorlds.Add(Context.World); return []; }
    }
}
