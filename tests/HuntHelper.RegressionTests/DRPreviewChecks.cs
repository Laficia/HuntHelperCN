using HuntHelper.Managers.Automation;
using HuntHelper.Managers.Hunts.Models;
using System.Numerics;

internal static class DRPreviewChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("DR preview appears only after an active DR area begins and has safe empty defaults", Lifecycle),
        ("DR preview rejects unknown foreign unloaded and mismatched map or instance contexts", Scope),
        ("DR preview coverage snapshots cannot mutate points or retroactively change earlier frames", PointSnapshots),
        ("DR preview target exists only while an owned observation path is moving", MovingTarget),
        ("DR preview includes only live A observations without recording or mutating discoveries", LiveObservations),
        ("DR preview disappears immediately on stop completion and a context interruption", Ends),
        ("DR preview hides during same-map and cross-map teleport and reappears after actual arrival", TeleportVisibility),
        ("DR preview discards old-server coverage and monster positions across a world queue", WorldQueue),
        ("DR preview marks the current four-hundred-metre circle immediately without completing observation", ImmediatePassedCircle),
        ("DR preview marks a passed target group before its required observation completes", PassedTargetGroup),
        ("DR preview marks points on the actual movement segment outside the current end circle", PassedMovementSegment),
        ("DR preview never joins a crystal teleport into a passed movement corridor", PassedTeleport),
        ("DR preview does not carry a stopped run or join it to a later start position", PassedRestart),
    ];

    private static void Check(bool condition, string message = "DR preview assertion failed")
    { if (!condition) throw new Exception(message); }
    private static PatrolRoute Route(params float[] x) => new()
    {
        TerritoryId = 10, MapId = 20, Expansion = 1, Name = "预览测试地图",
        Points = x.Select(value => new PatrolPoint { Position = new(value, 0, 0), ForA = true, ForB = false }).ToList(),
    };
    private static PatrolOptions Options() => new() { RouteMode = PatrolRouteMode.DRCoverage };
    private static (Host Host, PatrolSession Session) Start(PatrolRoute? route = null, Host? host = null, PatrolOptions? options = null)
    {
        host ??= new(); var session = new PatrolSession(host);
        Check(session.Start(new[] { route ?? Route(700, 800) }, options ?? Options(), 0), session.Message);
        return (host, session);
    }
    private static long Until(PatrolSession session, Func<bool> condition, long start = 0, long limit = 15000)
    {
        for (var now = start; now <= limit; now += 100)
        { session.Tick(now); if (condition()) return now; }
        throw new Exception("DR preview state was not reached: " + session.State + " / " + session.Message);
    }
    private static void Lifecycle()
    {
        var empty = new DRPatrolPreview();
        Check(empty.Points.Count == 0 && empty.Path.Count == 0 && empty.Monsters.Count == 0 &&
            empty.WorldName == "" && empty.MapName == "" && empty.Status == "" && empty.PathStatus == "" && empty.Target == null);
        var host = new Host(); var session = new PatrolSession(host);
        Check(session.GetDRPreview(host.Context) == null);
        var preset = Options(); preset.RouteMode = PatrolRouteMode.Preset;
        Check(session.Start(new[] { Route(700) }, preset, 0)); session.Tick(0);
        Check(session.GetDRPreview(host.Context) == null); session.Stop();
        Check(session.Start(new[] { Route(700, 800) }, Options(), 0));
        Check(session.GetDRPreview(host.Context) == null);
        session.Tick(0);
        var preview = session.GetDRPreview(host.Context)!;
        Check(preview is { World: 101, Territory: 10, Map: 20, Instance: 1, State: PatrolState.Scanning });
        Check(preview.MapName == "预览测试地图" && preview.Status == session.Message && preview.Player == host.Context.Position);
        Check(preview.Points.Count == 2 && preview.Points.All(p => !p.Covered) && preview.Target == null && preview.FoundA == 0);
        Check(preview.Path.Count == 0 && preview.Monsters.Count == 0);
        session.Stop();
    }
    private static void Scope()
    {
        var (host, session) = Start(); session.Tick(0);
        var contexts = new[]
        {
            host.Context with { World = 0 }, host.Context with { World = 102 },
            host.Context with { Territory = 11 }, host.Context with { Map = 21 },
            host.Context with { Instance = 2 }, host.Context with { Ready = false },
            host.Context with { LoggedIn = false }, host.Context with { Position = new(float.NaN, 0, 0) },
        };
        var scans = host.Scans;
        foreach (var context in contexts) Check(session.GetDRPreview(context) == null);
        Check(session.GetDRPreview(host.Context) != null && session.Active && host.Scans == scans);
        session.Stop();
    }
    private static void PointSnapshots()
    {
        var (host, session) = Start(Route(100, 700)); session.Tick(0);
        var initial = session.GetDRPreview(host.Context)!;
        var untouched = session.GetDRPreview(host.Context)!;
        Check(initial.Points is DRPreviewPoint[]);
        ((DRPreviewPoint[])initial.Points)[0] = new(new(9999, 0, 0), true);
        var fresh = session.GetDRPreview(host.Context)!;
        Check(fresh.Points[0].Position == new Vector3(100, 0, 0) && !fresh.Points[0].Covered);
        session.Tick(1500);
        var progressed = session.GetDRPreview(host.Context)!;
        Check(progressed.Points[0].Covered && !progressed.Points[1].Covered && progressed.Target == null);
        Check(untouched.Points.All(point => !point.Covered), "An older preview changed after patrol progress");
        Check(session.CompletedPoints == 1 && session.FoundA == 0 && host.Moves == 0);
        session.Stop();
    }
    private static void MovingTarget()
    {
        var (host, session) = Start(); var now = Until(session, () => host.Moves > 0);
        var moving = session.GetDRPreview(host.Context)!;
        Check(moving.State == PatrolState.Moving && moving.Target == host.Target);
        host.Context = host.Context with { Position = host.Target };
        session.Tick(now + 100);
        var observing = session.GetDRPreview(host.Context)!;
        Check(observing.State == PatrolState.Scanning && observing.Target == null && host.Stops > 0);
        Check(moving.Target == host.Target && moving.State == PatrolState.Moving, "A previous moving frame was mutated");
        session.Stop(); Check(session.GetDRPreview(host.Context) == null);
    }
    private static void LiveObservations()
    {
        var host = new Host { Known = new[] { new PatrolDetection(11, true, new(100, 0, 0)) } };
        var (_, session) = Start(host: host); session.Tick(0);
        var memory = session.GetDRPreview(host.Context)!;
        Check(memory.FoundA == 1 && memory.Monsters.Count == 0, "An old known record became a live marker");
        host.Found = new[]
        {
            new PatrolDetection(11, true, new(200, 10, 0)),
            new PatrolDetection(11, true, new(200, 10, 0)),
            new PatrolDetection(12, false, new(250, 10, 0)),
            new PatrolDetection(0, true, new(float.NaN, 0, 0)),
        };
        session.Tick(100);
        var observed = session.GetDRPreview(host.Context)!;
        Check(observed.FoundA == 1 && observed.Monsters.SequenceEqual(new[] { new Vector3(200, 10, 0) }));
        Check(observed.Monsters is Vector3[]);
        ((Vector3[])observed.Monsters)[0] = new(9999, 0, 0);
        var counts = (host.Scans, host.Moves, host.Teleports);
        for (var i = 0; i < 100; i++)
        {
            var preview = session.GetDRPreview(host.Context)!;
            Check(preview.Monsters.SequenceEqual(new[] { new Vector3(200, 10, 0) }) && preview.FoundA == 1);
        }
        Check(counts == (host.Scans, host.Moves, host.Teleports) && session.FoundA == 1 && session.CompletedPoints == 0,
            "Reading a preview recorded discoveries, moved the player, or covered a spawn");
        session.Stop();
    }
    private static void Ends()
    {
        var (host, session) = Start(Route(100)); session.Tick(0);
        Check(session.GetDRPreview(host.Context) != null); session.Tick(1500);
        Check(session.State == PatrolState.Completed && session.GetDRPreview(host.Context) == null);
        (host, session) = Start(); session.Tick(0); session.Stop();
        Check(session.GetDRPreview(host.Context) == null);
        (host, session) = Start(); session.Tick(0);
        host.Context = host.Context with { Instance = 2 }; session.Tick(100);
        Check(session.State == PatrolState.Stopped && session.GetDRPreview(host.Context) == null);
    }
    private static void TeleportVisibility()
    {
        var host = new Host
        {
            Crystals = new[] { new PatrolCoverageCrystal(new(7, "水晶", null), new(1000, 0)) },
        };
        var (_, session) = Start(Route(2000, 2100), host);
        var now = Until(session, () => host.Teleports > 0);
        Check(session.State == PatrolState.Teleporting && session.GetDRPreview(host.Context) == null);
        host.Context = host.Context with { Ready = false }; session.Tick(now + 100);
        Check(session.GetDRPreview(host.Context) == null);
        host.Context = host.Context with { Ready = true, Position = new(1000, 0, 0) }; session.Tick(now + 200);
        Check(session.GetDRPreview(host.Context) is { Player.X: 1000, State: PatrolState.Scanning });
        session.Stop();

        host = new() { EntryPlan = new(new(8, "跨图水晶", null), new(500, 0), float.PositiveInfinity, 10) };
        var target = Route(700); target.TerritoryId = 11; target.MapId = 21;
        (_, session) = Start(target, host); session.Tick(0);
        Check(session.GetDRPreview(host.Context) == null);
        host.Context = host.Context with { Ready = false }; session.Tick(100);
        host.Context = host.Context with { Ready = true, Territory = 11, Map = 21, Position = new(500, 0, 0) };
        Check(session.GetDRPreview(host.Context) == null); session.Tick(200);
        Check(session.GetDRPreview(host.Context) is { Territory: 11, Map: 21, State: PatrolState.Scanning });
        session.Stop();
    }
    private static void WorldQueue()
    {
        var options = Options(); options.Worlds = new() { new(101, "A服", "测试", 0), new(102, "B服", "测试", 0) };
        var host = new Host { Found = new[] { new PatrolDetection(11, true, new(100, 0, 0)) } };
        var (_, session) = Start(Route(100, 200), host, options);
        session.Tick(0); Check(session.GetDRPreview(host.Context) == null);
        session.Tick(100);
        Check(session.GetDRPreview(host.Context) is { World: 101, FoundA: 1, Monsters.Count: 1 });
        session.Tick(1600);
        Check(session.State == PatrolState.PreparingWorld && session.GetDRPreview(host.Context) == null);
        session.Tick(1700);
        Check(session.State == PatrolState.ChangingWorld && session.GetDRPreview(host.Context) == null);
        host.Found = Array.Empty<PatrolDetection>(); host.Context = host.Context with { World = 102, Position = new(1000, 0, 0) };
        Check(session.GetDRPreview(host.Context) == null);
        host.WorldTravelBusy = false; session.Tick(1800);
        Check(session.GetDRPreview(host.Context) == null); session.Tick(1900);
        var next = session.GetDRPreview(host.Context)!;
        Check(next.World == 102 && next.FoundA == 0 && next.Monsters.Count == 0 && next.Points.All(point => !point.Covered && !point.Passed));
        session.Stop();
    }
    private static void ImmediatePassedCircle()
    {
        var (host, session) = Start(Route(100, 400, 401, 700)); session.Tick(0);
        var preview = session.GetDRPreview(host.Context)!;
        Check(preview.Points.Select(point => point.Passed).SequenceEqual(new[] { true, true, false, false }));
        Check(preview.Points.All(point => !point.Covered) && session.CompletedPoints == 0 && session.FoundA == 0 && host.Moves == 0,
            "Immediate passed colour completed a scan or invented a discovery");
        session.Tick(1000);
        Check(session.GetDRPreview(host.Context)!.Points.All(point => !point.Covered) && session.CompletedPoints == 0);
        session.Stop();
    }
    private static void PassedTargetGroup()
    {
        var (host, session) = Start(); var now = Until(session, () => host.Moves > 0);
        host.Context = host.Context with { Position = new(320, 0, 0) }; session.Tick(now + 100);
        var passing = session.GetDRPreview(host.Context)!;
        Check(passing.State == PatrolState.Moving && passing.Points[0] is { Passed: true, Covered: false });
        Check(passing.Points[1] is { Passed: false, Covered: false } && session.CompletedPoints == 0 && session.FoundA == 0);
        ((DRPreviewPoint[])passing.Points)[0] = new(new(9999, 0, 0), true, false);
        Check(session.GetDRPreview(host.Context)!.Points[0] is { Passed: true, Covered: false, Position.X: 700 },
            "Editing a passed-point snapshot changed the active scan");
        host.Context = host.Context with { Position = host.Target }; session.Tick(now + 200);
        var observing = session.GetDRPreview(host.Context)!;
        Check(observing.State == PatrolState.Scanning && observing.Points[0] is { Passed: true, Covered: false });
        Check(session.CompletedPoints < 2);
        session.Stop();
    }
    private static void PassedMovementSegment()
    {
        var (host, session) = Start(Route(700, 2000)); var now = Until(session, () => host.Moves > 0);
        host.Context = host.Context with { Position = new(1200, 0, 0) }; session.Tick(now + 100);
        var preview = session.GetDRPreview(host.Context)!;
        Check(Vector3.Distance(preview.Player, preview.Points[0].Position) > DRCoveragePlanner.DetectionRadius);
        Check(preview.Points[0] is { Passed: true, Covered: false } && preview.Points[1] is { Passed: false, Covered: false });
        Check(session.CompletedPoints == 0 && session.FoundA == 0);
        session.Stop();
    }
    private static void PassedTeleport()
    {
        // Nearby observation destinations are unavailable. The valid alternative
        // reaches both spawn locations more cheaply through the unlocked crystal.
        var host = new Host
        {
            Crystals = new[] { new PatrolCoverageCrystal(new(7, "水晶", null), new(1000, 0)) },
            DestinationAllowed = point => point.X >= 600,
        };
        var (_, session) = Start(Route(500, 800), host); var now = Until(session, () => host.Teleports > 0);
        Check(host.Moves == 0 && session.GetDRPreview(host.Context) == null);
        host.Context = host.Context with { Ready = false }; session.Tick(now + 100);
        host.Context = host.Context with { Ready = true, Position = new(1000, 0, 0) }; session.Tick(now + 200);
        var preview = session.GetDRPreview(host.Context)!;
        Check(preview.Points[0] is { Passed: false, Covered: false },
            "A point between departure and arrival was marked passed by teleporting over it");
        Check(preview.Points[1] is { Passed: true, Covered: false } && session.CompletedPoints == 0 && session.FoundA == 0);
        session.Stop();
    }
    private static void PassedRestart()
    {
        var route = Route(100, 500, 1000); var (host, session) = Start(route); session.Tick(0);
        var stopped = session.GetDRPreview(host.Context)!;
        Check(stopped.Points.Select(point => point.Passed).SequenceEqual(new[] { true, false, false }));
        session.Stop(); host.Context = host.Context with { Position = new(1000, 0, 0), Instance = 2 }; session.Tick(100);
        Check(session.GetDRPreview(host.Context) == null);
        Check(stopped.Points.Select(point => point.Passed).SequenceEqual(new[] { true, false, false }));
        Check(session.Start(new[] { route }, Options(), 200)); session.Tick(200);
        var restarted = session.GetDRPreview(host.Context)!;
        Check(restarted.Instance == 2 && restarted.Points.Select(point => point.Passed).SequenceEqual(new[] { false, false, true }),
            "Restart retained old passed points or bridged the two runs");
        Check(restarted.Points.All(point => !point.Covered) && session.CompletedPoints == 0 && session.FoundA == 0);
        session.Stop();
    }

    private sealed class Host : IPatrolHost
    {
        public PatrolContext Context { get; set; } = new(101, 10, 20, 1, Vector3.Zero, true, true, false, false);
        public bool Navigating { get; set; }
        public string NavigationMessage => "导航停止";
        public string WorldTravelError => "";
        public bool WorldTravelBusy { get; set; }
        public int Moves, Scans, Teleports, Stops;
        public Vector3 Target;
        public PatrolTeleport? EntryPlan;
        public Func<Vector3, bool>? DestinationAllowed;
        public IReadOnlyList<PatrolCoverageCrystal> Crystals = Array.Empty<PatrolCoverageCrystal>();
        public IReadOnlyList<PatrolDetection> Known = Array.Empty<PatrolDetection>(), Found = Array.Empty<PatrolDetection>();
        public bool BeginCoverage(out string error) { error = ""; return true; }
        public void EndCoverage() { }
        public bool TryCoverageDestination(Vector3 candidate, out Vector3 destination)
        { destination = candidate; return DestinationAllowed?.Invoke(candidate) ?? true; }
        public IReadOnlyList<PatrolCoverageCrystal> CoverageCrystals(PatrolRoute route) => Crystals;
        public bool Teleport(PatrolRoute route, out string error) { Teleports++; error = ""; return true; }
        public PatrolTeleport? PlanTeleport(PatrolRoute route, PatrolPoint point, PatrolOptions options) => EntryPlan;
        public bool PrepareMount(bool mount, bool fly, out string message) { message = ""; return true; }
        public bool Move(PatrolRoute route, PatrolPoint point, out Vector3 destination, out string error)
        { Moves++; destination = Target = point.Position!.Value; error = ""; Navigating = true; return true; }
        public void StopNavigation() { Stops++; Navigating = false; }
        public IReadOnlyList<PatrolDetection> Scan(bool rankA, bool rankB) { Scans++; return Found; }
        public IReadOnlyList<PatrolDetection> KnownDetections(bool rankA, bool rankB) => Known;
        public string WorldVisitProblem(HuntTrainWorld world) => "";
        public bool ChangeWorld(HuntTrainWorld world, out string error) { WorldTravelBusy = true; error = ""; return true; }
        public void AbortWorldTravel() => WorldTravelBusy = false;
    }
}
