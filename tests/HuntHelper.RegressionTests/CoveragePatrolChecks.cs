using HuntHelper;
using HuntHelper.Managers.Automation;
using HuntHelper.Managers.Hunts.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Numerics;
using System.Security.Cryptography;

internal static class CoveragePatrolChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("DR point data is the exact pinned resource with all 47 maps and 924 three-dimensional points", PinnedData),
        ("DR points retain server-independent territory metadata and A-only rank filtering", PointMetadata),
        ("DR malformed partial duplicate and non-finite data fails without substituting presets", InvalidData),
        ("missing DR data cannot silently become a valid empty or preset route set", MissingData),
        ("DR observation waits before covering multiple spawn points without inventing discoveries", ObserveMultiple),
        ("DR observation restarts its loading wait after the player moves", ObserveStability),
        ("DR coverage navigation stops near an observation point instead of the monster's exact coordinates", NearbyObservation),
        ("DR uses normal crystal travel only after comparing coverage and travel cost", CoverageTeleport),
        ("DR same-map teleport waits for busy and arrival and rejects a changed instance", TeleportGuard),
        ("DR respects a native navigation cancellation and never starts a replacement path", NativeStop),
        ("DR world map instance combat death and logout interruptions restore the range lease", ContextGuards),
        ("DR uses existing complete live records without unnecessary travel", CompleteRecords),
        ("DR coverage and species counts reset between queued worlds", MultiWorldIsolation),
        ("the original preset mode never acquires DR display settings", PresetUnaffected),
        ("DR display-range setup failure cannot start movement or leave an owned lease", RangeFailure),
        ("DR rejects missing three-dimensional points invalid ranks and disabled flight before acquiring settings", InvalidModeInputs),
        ("DR unavailable navigation candidates stop without silently claiming unvisited points", UnreachableCandidates),
        ("DR mount preparation and stalled movement have bounded timeouts and restore settings", StepTimeouts),
        ("DR running mode and three-dimensional points are snapshots independent of later UI edits", Snapshot),
        ("DR unknown current world pauses scanning and navigation before selecting an observation point", UnknownWorld),
        ("DR native stop takes precedence over a simultaneous complete A-rank discovery", NativeStopBeforeCompletion),
        ("DR cross-map travel requires a loading transition and actual known-crystal arrival", CrossMapArrival),
    ];

    private static string DataPath => Path.Combine(AppContext.BaseDirectory, "DRSpawnPoints.json");
    private static string Data => File.ReadAllText(DataPath);
    private static void Check(bool condition, string message = "DR coverage assertion failed")
    { if (!condition) throw new Exception(message); }

    private static List<PatrolRoute> Metadata() => JObject.Parse(Data).Properties().Select((p, i) =>
        new PatrolRoute { TerritoryId = uint.Parse(p.Name), MapId = uint.Parse(p.Name) + 1000,
            Name = "测试区域" + p.Name, Expansion = i % 6 }).Reverse().ToList();

    private static void PinnedData()
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(DataPath)));
        Check(hash == "046BB96522F172FF76AED7AC57078B160E59BE8C02073123C91BA5F10C58E428", "DR resource differs from the user-selected commit");
        Check(DRPatrolRoutes.TryLoad(DataPath, Metadata(), out var routes, out var error), error);
        Check(routes.Count == 47 && routes.Sum(r => r.Points.Count) == 924);
        Check(routes.All(r => r.Points.All(p => p.Position.HasValue && p.ForA && !p.ForB)));
        foreach (var property in JObject.Parse(Data).Properties())
        {
            var route = routes.Single(r => r.TerritoryId == uint.Parse(property.Name));
            var expected = ((JArray)property.Value).Select(p => p["Position"]!.ToObject<Vector3>()).ToList();
            Check(route.Points.Select(p => p.Position!.Value).SequenceEqual(expected), "DR point coordinates or order were changed");
        }
    }

    private static void PointMetadata()
    {
        var maps = Metadata();
        Check(DRPatrolRoutes.TryParse(Data, maps, out var routes, out var error), error);
        foreach (var route in routes)
        {
            var metadata = maps.Single(m => m.TerritoryId == route.TerritoryId);
            Check(route.MapId == metadata.MapId && route.Name == metadata.Name && route.Expansion == metadata.Expansion);
            Check(!ReferenceEquals(route, metadata) && !ReferenceEquals(route.Points, metadata.Points));
        }
        Check(maps.All(m => m.Points.Count == 0), "Loading DR points mutated the existing preset maps");
    }

    private static void InvalidData()
    {
        var original = JObject.Parse(Data);
        void Reject(Action<JObject> mutate)
        {
            var document = (JObject)original.DeepClone(); mutate(document);
            Check(!DRPatrolRoutes.TryParse(document.ToString(Formatting.None), Metadata(), out var routes, out var error));
            Check(routes.Count == 0 && !string.IsNullOrWhiteSpace(error), "Invalid DR data produced usable routes or no explanation");
        }
        static JArray First(JObject document) => (JArray)document.Properties().First().Value;
        Reject(d => d.Properties().First().Remove());
        Reject(d => First(d).RemoveAt(0));
        Reject(d => First(d)[1] = First(d)[0].DeepClone());
        Reject(d => First(d)[0]["Zone"] = 99999);
        Reject(d => ((JObject)First(d)[0]["Position"]!).Remove("Y"));
        Reject(d => First(d)[0]["Position"]!["X"] = 10001);
        Reject(d => First(d)[0]["Position"]!["Y"] = double.NaN);
        Reject(d => First(d)[0]["Position"]!["Z"] = double.PositiveInfinity);
        foreach (var json in new[] { "", "null", "[]", "{}", "{invalid" })
            Check(!DRPatrolRoutes.TryParse(json, Metadata(), out var routes, out var error) && routes.Count == 0 && error.Length > 0);
        var missingMetadata = Metadata(); missingMetadata.RemoveAt(0);
        Check(!DRPatrolRoutes.TryParse(Data, missingMetadata, out var noRoutes, out _) && noRoutes.Count == 0);
    }

    private static void MissingData()
    {
        var missing = Path.Combine(Path.GetTempPath(), "HuntHelper-missing-DR-" + Guid.NewGuid() + ".json");
        Check(!DRPatrolRoutes.TryLoad(missing, Metadata(), out var routes, out var error));
        Check(routes.Count == 0 && !string.IsNullOrWhiteSpace(error));
    }

    private static PatrolRoute Route(params float[] x) => new()
    {
        TerritoryId = 10, MapId = 20, Expansion = 1, Name = "DR测试图",
        Points = x.Select(value => new PatrolPoint { Position = new Vector3(value, 0, 0), ForA = true, ForB = false }).ToList(),
    };
    private static PatrolOptions Options() => new()
    {
        RouteMode = PatrolRouteMode.DRCoverage, RankA = true, RankB = false,
        Mount = true, Takeoff = true, SmartTeleport = true, TeleportSeconds = 10,
    };
    private static (Host Host, PatrolSession Session) Start(PatrolRoute? route = null, PatrolOptions? options = null, Host? host = null)
    {
        host ??= new Host(); var session = new PatrolSession(host);
        Check(session.Start(new[] { route ?? Route(700, 800) }, options ?? Options(), 0), session.Message);
        return (host, session);
    }
    private static long Until(PatrolSession session, Func<bool> condition, long start = 0, long limit = 15000)
    {
        for (var now = start; now <= limit; now += 100)
        { session.Tick(now); if (condition()) return now; }
        throw new Exception("DR patrol did not reach expected state: " + session.State + " / " + session.Message);
    }
    private static void ObserveMultiple()
    {
        var (host, session) = Start(Route(100, 250, 399));
        session.Tick(0); session.Tick(100); session.Tick(1000);
        Check(session.CompletedPoints == 0 && session.Active && host.Moves == 0);
        Until(session, () => !session.Active, 1100);
        Check(session.State == PatrolState.Completed && session.FoundA == 0 && host.Moves == 0 && host.ScannedWorlds.Count > 0);
        Check(host.Begins == 1 && host.Ends == 1, "Normal completion did not restore DR display settings exactly once");
    }
    private static void ObserveStability()
    {
        var (host, session) = Start(Route(100, 250));
        var observing = Until(session, () => session.State == PatrolState.Scanning);
        session.Tick(observing + 1000);
        host.Context = host.Context with { Position = new(2, 0, 0) };
        session.Tick(observing + 1100); session.Tick(observing + 2000);
        Check(session.Active && session.CompletedPoints == 0, "Moved player was treated as a stable observation");
        Until(session, () => !session.Active, observing + 2700);
        Check(session.State == PatrolState.Completed && host.Moves == 0);
    }
    private static void NearbyObservation()
    {
        var (host, session) = Start();
        var now = Until(session, () => host.Moves > 0);
        Check(host.Target.X < 650 && host.Target.X > 0, "Coverage mode still flew to the monster's exact spawn coordinates");
        Check(session.CompletedPoints == 0 && session.FoundA == 0);
        host.Context = host.Context with { Position = host.Target };
        session.Tick(now + 100);
        // DR can cover other spawn points along the flown segment, but the target
        // group must survive until the stationary observation has finished.
        Check(session.State == PatrolState.Scanning && session.CompletedPoints < 2 && host.Stops > 0);
        Until(session, () => !session.Active, now + 200);
        Check(session.State == PatrolState.Completed && host.Moves == 1 && session.FoundA == 0);
    }
    private static (Host Host, PatrolSession Session, long Now) StartTeleport()
    {
        var host = new Host { Crystals = new[] { new PatrolCoverageCrystal(new(7, "测试水晶", null), new(1000, 0)) } };
        var (_, session) = Start(Route(2000, 2100), host: host);
        var now = Until(session, () => host.Teleports > 0);
        return (host, session, now);
    }
    private static void CoverageTeleport()
    {
        var (host, session, now) = StartTeleport();
        Check(host.LastTeleport?.Crystal.Id == 7 && host.LastTeleport.WorldPosition == new Vector2(1000, 0) && host.Moves == 0);
        host.Context = host.Context with { Ready = false }; session.Tick(now + 100);
        host.Context = host.Context with { Ready = true, Position = new(1000, 0, 0) }; session.Tick(now + 200);
        Until(session, () => host.Moves > 0, now + 300);
        Check(host.Teleports == 1 && host.Target.X < 2000);
        session.Stop(); Check(host.Ends == 1);
        var options = Options(); options.SmartTeleport = false; options.TeleportSeconds = 120;
        var changedPresetHost = new Host { Crystals = host.Crystals };
        var (_, independent) = Start(Route(2000, 2100), options, changedPresetHost);
        Until(independent, () => changedPresetHost.Teleports > 0);
        Check(changedPresetHost.Moves == 0 && changedPresetHost.LastTeleport?.Crystal.Id == 7 &&
            changedPresetHost.LastTeleport.ViaSeconds == host.LastTeleport!.ViaSeconds,
            "Hidden preset-mode teleport preferences changed the DR fixed-cost planner");
        independent.Stop();
    }
    private static void TeleportGuard()
    {
        var (host, session, now) = StartTeleport();
        session.Tick(now + 100); session.Tick(now + 1000);
        Check(session.Active && host.Moves == 0 && session.CompletedPoints == 0);
        host.Context = host.Context with { Position = new(1000, 0, 0) }; session.Tick(now + 1100);
        Check(host.Moves == 0 && session.CompletedPoints == 0, "Same-map teleport completed without a loading transition");
        var scans = host.ScannedWorlds.Count;
        host.Context = host.Context with { Instance = 2 }; session.Tick(now + 1200);
        Check(!session.Active && host.Moves == 0 && host.ScannedWorlds.Count == scans && host.Ends == 1);
    }
    private static void NativeStop()
    {
        var (host, session) = Start(); var now = Until(session, () => host.Moves > 0);
        host.Navigating = false; host.NavigationInterrupted = true;
        session.Tick(now + 100); session.Tick(now + 3000); session.Tick(now + 10000);
        Check(!session.Active && host.Moves == 1 && session.CompletedPoints == 0 && host.Ends == 1);
    }
    private static void ContextGuards()
    {
        var changes = new Func<PatrolContext, PatrolContext>[]
        {
            context => context with { World = 102 }, context => context with { Territory = 11 },
            context => context with { Map = 21 }, context => context with { Instance = 2 },
            context => context with { InCombat = true }, context => context with { Dead = true },
            context => context with { LoggedIn = false },
        };
        foreach (var change in changes)
        {
            var (host, session) = Start(); var now = Until(session, () => host.Moves > 0);
            var scans = host.ScannedWorlds.Count; host.Context = change(host.Context); session.Tick(now + 100);
            Check(!session.Active && host.Ends == 1 && host.Moves == 1 && host.ScannedWorlds.Count == scans);
            session.Stop(); Check(host.Ends == 1, "Repeated Stop restored a range lease twice");
        }
    }
    private static void CompleteRecords()
    {
        var host = new Host { Known = new[] { new PatrolDetection(1, true), new PatrolDetection(2, true) } };
        var (_, session) = Start(host: host); Until(session, () => !session.Active);
        Check(session.State == PatrolState.Completed && host.Moves == 0 && host.Teleports == 0 && host.ScannedWorlds.Count == 0);
        Check(host.Begins == host.Ends);
    }
    private static void MultiWorldIsolation()
    {
        var host = new Host { Known = new[] { new PatrolDetection(1, true), new PatrolDetection(2, true) } };
        var options = Options(); options.Worlds = new() { new(101, "A服", "测试", 0), new(102, "B服", "测试", 0) };
        var (_, session) = Start(Route(100, 200), options, host);
        var now = Until(session, () => session.State == PatrolState.ChangingWorld);
        Check(host.Changes.SequenceEqual(new uint[] { 102 }) && host.ScannedWorlds.Count == 0);
        host.Known = Array.Empty<PatrolDetection>(); host.WorldTravelBusy = false;
        host.Context = host.Context with { World = 102 };
        Until(session, () => !session.Active, now + 100);
        Check(session.State == PatrolState.Completed && session.FoundA == 0 && host.ScannedWorlds.Count > 0 && host.ScannedWorlds.All(world => world == 102));
        Check(host.Begins == host.Ends);
    }
    private static void PresetUnaffected()
    {
        var options = Options(); options.RouteMode = PatrolRouteMode.Preset;
        var (host, session) = Start(Route(900), options);
        Until(session, () => host.Moves > 0);
        Check(host.Target.X == 900 && host.Begins == 0 && host.Ends == 0);
        session.Stop(); Check(host.Ends == 0);
    }
    private static void RangeFailure()
    {
        var host = new Host { RangeAvailable = false }; var session = new PatrolSession(host);
        var started = session.Start(new[] { Route(700) }, Options(), 0);
        if (started) Until(session, () => !session.Active);
        Check(host.Begins == 1 && host.Ends == 0 && host.Moves == 0 && host.Teleports == 0);
        Check(session.Message.Contains("显示范围"));
    }
    private static void InvalidModeInputs()
    {
        foreach (var mutate in new Action<PatrolOptions>[]
            { o => o.RankA = false, o => o.RankB = true, o => o.Mount = false,
              o => o.Takeoff = false, o => o.RouteMode = (PatrolRouteMode)99 })
        {
            var options = Options(); mutate(options); var host = new Host(); var session = new PatrolSession(host);
            Check(!session.Start(new[] { Route(700) }, options, 0) && host.Begins == 0 && host.Moves == 0);
        }
        var mapOnly = Route(700); mapOnly.Points[0].Position = null; mapOnly.Points[0].MapPosition = new(20, 20);
        var missingHeight = new Host();
        Check(!new PatrolSession(missingHeight).Start(new[] { mapOnly }, Options(), 0) && missingHeight.Begins == 0);
    }
    private static void UnreachableCandidates()
    {
        var host = new Host { DestinationAvailable = false }; var (_, session) = Start(host: host);
        Until(session, () => !session.Active);
        Check(session.State == PatrolState.Stopped && host.Queries > 0 && host.Moves == 0 && host.Ends == 1);
        Check(session.FoundA == 0 && !session.Message.Contains("覆盖完成"));
    }
    private static void StepTimeouts()
    {
        var host = new Host { MountReady = false }; var (_, session) = Start(host: host);
        var now = Until(session, () => session.State == PatrolState.PreparingMove);
        session.Tick(now + 180001);
        Check(!session.Active && host.Moves == 0 && host.Ends == 1);
        (host, session) = Start(); now = Until(session, () => host.Moves > 0);
        session.Tick(now + 15001);
        Check(!session.Active && host.Moves == 1 && host.Ends == 1 && host.Stops > 0);
    }
    private static void Snapshot()
    {
        var route = Route(700, 800); var options = Options(); var (host, session) = Start(route, options);
        options.RouteMode = PatrolRouteMode.Preset; options.RankA = false;
        route.Points[0].Position = new(9000, 0, 0); route.Points.Clear();
        Until(session, () => host.Moves > 0);
        Check(session.RouteMode == PatrolRouteMode.DRCoverage && host.Target.X < 650 && session.CurrentRoute!.Points.Count == 2);
        session.Stop(); Check(host.Ends == 1);
    }
    private static void UnknownWorld()
    {
        var (host, session) = Start(); var now = Until(session, () => session.State == PatrolState.PreparingMove);
        var scans = host.ScannedWorlds.Count;
        host.Context = host.Context with { World = 0 };
        session.Tick(now + 100);
        Check(host.Moves == 0 && host.ScannedWorlds.Count == scans && session.CompletedPoints == 0,
            "DR navigation continued while the current server was unknown");
        host.Context = host.Context with { World = 101 };
        Until(session, () => host.Moves > 0, now + 200);
        Check(host.Moves == 1 && host.ScannedWorlds.All(world => world == 101));
        session.Stop(); Check(host.Ends == 1);
    }
    private static void NativeStopBeforeCompletion()
    {
        var host = new Host(); var session = new PatrolSession(host);
        var first = Route(700, 800); var next = Route(100);
        next.TerritoryId = 11; next.MapId = 21;
        Check(session.Start(new[] { first, next }, Options(), 0), session.Message);
        var now = Until(session, () => host.Moves > 0);
        var scans = host.ScannedWorlds.Count;
        host.Found = new[] { new PatrolDetection(1, true), new PatrolDetection(2, true) };
        host.NavigationInterrupted = true; host.Navigating = false;
        session.Tick(now + 100); session.Tick(now + 1000); session.Tick(now + 5000);
        Check(session.State == PatrolState.Stopped && session.CurrentRoute?.TerritoryId == 10,
            "A simultaneous complete discovery advanced the route after a native stop");
        Check(host.Moves == 1 && host.Teleports == 0 && host.ScannedWorlds.Count == scans && host.Ends == 1,
            "Native stop allowed a scan, replacement path or next-map teleport");
    }
    private static void CrossMapArrival()
    {
        foreach (var busyByUnknownWorld in new[] { false, true })
        {
            var host = new Host
            {
                EntryPlan = new(new(8, "跨图水晶", null), new(500, 0), float.PositiveInfinity, 10),
            };
            var target = Route(700); target.TerritoryId = 11; target.MapId = 21;
            var (_, session) = Start(target, host: host);
            session.Tick(0);
            Check(session.State == PatrolState.Teleporting && host.Teleports == 1 && host.ScannedWorlds.Count == 0);
            host.Context = host.Context with { Territory = 11, Map = 21, Position = new(500, 0, 0) };
            session.Tick(100); session.Tick(200);
            Check(session.State == PatrolState.Teleporting && host.ScannedWorlds.Count == 0 && host.Queries == 0 && host.Moves == 0,
                "A map ID update without loading prematurely started DR coverage");
            host.Context = busyByUnknownWorld
                ? host.Context with { World = 0 }
                : host.Context with { Ready = false };
            session.Tick(300);
            host.Context = host.Context with { World = 101, Ready = true, Position = Vector3.Zero };
            session.Tick(400); session.Tick(500);
            Check(session.State == PatrolState.Teleporting && host.ScannedWorlds.Count == 0 && host.Moves == 0,
                "Loading alone was treated as arrival despite being far from the known crystal");
            host.Context = host.Context with { Position = new(401, 0, 0) };
            session.Tick(600);
            Check(session.State == PatrolState.Scanning && host.ScannedWorlds.Count == 0 && host.Moves == 0);
            session.Tick(700);
            Check(host.ScannedWorlds.SequenceEqual(new uint[] { 101 }) && session.CompletedPoints == 0);
            session.Stop(); Check(host.Ends == 1);
        }
    }
    private sealed class Host : IPatrolHost
    {
        public PatrolContext Context { get; set; } = new(101, 10, 20, 1, Vector3.Zero, true, true, false, false);
        public bool Navigating { get; set; }
        public bool NavigationInterrupted { get; set; }
        public string NavigationMessage => "测试导航已停止";
        public string WorldTravelError => "";
        public bool WorldTravelBusy { get; set; }
        public bool RangeAvailable = true, DestinationAvailable = true, MountReady = true;
        public int Begins, Ends, Moves, Teleports, Stops, Queries;
        public List<uint> Changes = new(), ScannedWorlds = new();
        public Vector3 Target;
        public PatrolTeleport? LastTeleport, EntryPlan;
        public IReadOnlyList<PatrolCoverageCrystal> Crystals = Array.Empty<PatrolCoverageCrystal>();
        public IReadOnlyList<PatrolDetection> Known = Array.Empty<PatrolDetection>();
        public IReadOnlyList<PatrolDetection> Found = Array.Empty<PatrolDetection>();
        public bool BeginCoverage(out string error) { Begins++; error = RangeAvailable ? "" : "无法准备显示范围"; return RangeAvailable; }
        public void EndCoverage() => Ends++;
        public bool TryCoverageDestination(Vector3 candidate, out Vector3 destination) { Queries++; destination = candidate; return DestinationAvailable; }
        public IReadOnlyList<PatrolCoverageCrystal> CoverageCrystals(PatrolRoute route) => Crystals;
        public float CoverageSpeed(PatrolOptions options) => 12;
        public bool Teleport(PatrolRoute route, out string error) => Teleport(route, null, out error);
        public bool Teleport(PatrolRoute route, PatrolTeleport? plan, out string error)
        { Teleports++; LastTeleport = plan; error = ""; return true; }
        public PatrolTeleport? PlanTeleport(PatrolRoute route, PatrolPoint point, PatrolOptions options) => EntryPlan;
        public bool PrepareMount(bool mount, bool fly, out string message) { message = ""; return MountReady; }
        public bool Move(PatrolRoute route, PatrolPoint point, out Vector3 destination, out string error)
        { Moves++; destination = Target = point.Position!.Value; error = ""; Navigating = true; return true; }
        public void StopNavigation() { Stops++; Navigating = false; }
        public IReadOnlyList<PatrolDetection> Scan(bool rankA, bool rankB) { ScannedWorlds.Add(Context.World); return Found; }
        public IReadOnlyList<PatrolDetection> KnownDetections(bool rankA, bool rankB) => Known;
        public string WorldVisitProblem(HuntTrainWorld world) => "";
        public bool ChangeWorld(HuntTrainWorld world, out string error)
        { Changes.Add(world.WorldID); WorldTravelBusy = true; error = ""; return true; }
        public void AbortWorldTravel() => WorldTravelBusy = false;
    }
}
