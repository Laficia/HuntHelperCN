using System.Numerics;
using HuntHelper.Managers.Automation;
using HuntHelper.Utilities;

internal static class DRRulesChecks
{
    internal static readonly (string Name, Action Run)[] Tests = [
        ("DR candidate order and voxel altitude use the original point order and player height", CandidateOrder),
        ("DR route scores use resolved voxel geometry without extra height restrictions", ResolvedGeometry),
        ("DR compares only the nearest permitted crystal to each resolved destination", NearestCrystal),
        ("DR entry crystal is nearest to any spawn rather than the first serialized point", EntryCrystal),
        ("DR retry geometry and timing match the pinned official scanner", RetryRules),
    ];
    private static void Check(bool ok) { if (!ok) throw new Exception("DR scanner rules differ from pinned source"); }
    private static void CandidateOrder()
    {
        var inputs = new List<Vector3>();
        var points = new[] { new Vector3(700, 40, 0), new Vector3(800, -170, 0) };
        _ = DRCoveragePlanner.Plan(new(0, 37, 0), points, new[] { 0, 1 }, [], p => { inputs.Add(p); return p; }).ToArray();
        Check(inputs.Count == 7 && inputs.Take(2).SequenceEqual(new[] { new Vector3(700, 37, 0), new Vector3(800, 37, 0) }));
        Check(Vector3.Distance(inputs[2], new(420, 37, 0)) < .001f && Vector3.Distance(inputs[3], new(520, 37, 0)) < .001f);
        Check(inputs.All(p => p.Y == 37));
    }
    private static void ResolvedGeometry()
    {
        var plans = DRCoveragePlanner.Plan(Vector3.Zero, [new(700, 1, 0)], new[] { 0 }, [], _ => new Vector3(410, -77, 0))
            .Where(p => p != null).Select(p => p!).ToArray();
        Check(plans.Length == 2 && plans.All(p => p.Destination == new Vector3(410, -77, 0) &&
            p.Coverage == 1 && Math.Abs(p.Seconds - (410f / 12 + .25f)) < .001f));
        Check(!DRCoveragePlanner.Plan(Vector3.Zero, [new(700, 1, 0)], new[] { 0 }, [], _ => new Vector3(1100, 0, 0)).Any(p => p != null));
    }
    private static void NearestCrystal()
    {
        var crystals = new[] {
            new PatrolCoverageCrystal(new(1, "nearest", null), new(500, 900)),
            new PatrolCoverageCrystal(new(2, "farther but more coverage", null), new(1600, 0)) };
        var plans = DRCoveragePlanner.Plan(new(-2000, 0, 0), [new(700, 0, 0), new(1500, 0, 0)], new[] { 0, 1 }, crystals,
            _ => new Vector3(500, 0, 0)).Where(p => p != null).Select(p => p!).ToArray();
        Check(plans.Length > 0 && plans.All(p => p.Crystal?.Crystal.Id == 1 && Math.Abs(p.Seconds - 85.5f) < .001f));
    }
    private static void EntryCrystal()
    {
        var a = new PatrolCrystal(1, "first spawn", new(15, 15)); var b = new PatrolCrystal(2, "later spawn", new(30, 30));
        var aPos = NavigationRules.FromHuntMapCoordinates(a.Position!.Value, 100);
        var bPos = NavigationRules.FromHuntMapCoordinates(b.Position!.Value, 100);
        var route = new PatrolRoute { TerritoryId = 613, MapId = 100, Points = new() {
            new() { Position = aPos + new Vector3(100, 0, 0) }, new() { Position = bPos + new Vector3(1, 0, 0) } } };
        var c = new PatrolContext(1, 10, 20, 0, Vector3.Zero, true, true, false, false);
        Check(PatrolTravelPlanner.Select(c, route, route.Points[0], new() { RouteMode = PatrolRouteMode.DRCoverage },
            new[] { a, b }, 100, 12)?.Crystal.Id == 2);
    }
    private static void RetryRules()
    {
        var target = new Vector3(10, 37, 20);
        var retries = DRCoveragePlanner.RetryTargets(target, target).ToArray();
        Check(retries.Length == 19 && retries[0] == target && retries[1] == target with { Y = 1024 } &&
            retries[^1] == target with { Y = -1024 });
        Check(retries.Skip(2).Take(8).All(p => Math.Abs(DRCoveragePlanner.DistanceSquared(p, target) - 6400) < .01f));
        Check(DRCoveragePlanner.ObservationMs == 250 && DRCoveragePlanner.DetectionRadius == 400 &&
            DRCoveragePlanner.PlanningRadius == 300 && DRCoveragePlanner.ArrivalRadius == 20);
    }
}
