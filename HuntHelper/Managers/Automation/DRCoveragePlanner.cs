using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

public sealed record DRCoveragePlan(int Anchor, Vector3 Destination, PatrolCoverageCrystal? Crystal,
    int Coverage, float Seconds, float Score);

// Independently implemented coverage geometry, following DR AutoMarksFinder's
// observation-point/cost-per-covered-spawn approach. The input is its pinned 3D data.
public static class DRCoveragePlanner
{
    public const float DetectionRadius = 400, PlanningRadius = 300, ArrivalRadius = 20;
    public const int ObservationMs = 1500;

    public static float DistanceSquared(Vector3 a, Vector3 b) =>
        Vector2.DistanceSquared(new(a.X, a.Z), new(b.X, b.Z));

    public static float SegmentDistanceSquared(Vector3 p, Vector3 a, Vector3 b)
    {
        var line = new Vector2(b.X - a.X, b.Z - a.Z);
        var offset = new Vector2(p.X - a.X, p.Z - a.Z);
        var t = line.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(offset, line) / line.LengthSquared(), 0, 1) : 0;
        return (offset - t * line).LengthSquared();
    }

    public static IReadOnlyList<DRCoveragePlan> Plan(Vector3 player, IReadOnlyList<Vector3> points,
        IReadOnlyCollection<int> pending, IReadOnlyList<PatrolCoverageCrystal> crystals,
        float speed, float teleportSeconds)
    {
        if (pending.Count == 0) return Array.Empty<DRCoveragePlan>();
        speed = float.IsFinite(speed) ? Math.Clamp(speed, 6, 30) : 12;
        teleportSeconds = float.IsFinite(teleportSeconds) ? Math.Clamp(teleportSeconds, 10, 120) : 25;
        var anchor = pending.OrderBy(i => DistanceSquared(points[i], player)).ThenBy(i => i).First();
        var nearby = pending.Where(i => DistanceSquared(points[i], points[anchor]) <= 4 * PlanningRadius * PlanningRadius)
            .OrderBy(i => i).Select(i => points[i]).ToArray();
        var candidates = new List<Vector3>();
        foreach (var p in nearby)
        {
            candidates.Add(p);
            var distance = MathF.Sqrt(DistanceSquared(player, p));
            if (distance > 0) candidates.Add(Vector3.Lerp(p, player, Math.Min((PlanningRadius - ArrivalRadius) / distance, 1)));
        }
        for (var i = 0; i < nearby.Length; i++)
        for (var j = i + 1; j < nearby.Length; j++)
        {
            var a = nearby[i]; var b = nearby[j];
            var d2 = DistanceSquared(a, b);
            if (d2 <= 0 || d2 > 4 * PlanningRadius * PlanningRadius) continue;
            var middle = (a + b) / 2;
            var perpendicular = new Vector3(a.Z - b.Z, 0, b.X - a.X) * MathF.Sqrt(Math.Max(0, PlanningRadius * PlanningRadius / d2 - .25f));
            candidates.Add(middle); candidates.Add(middle + perpendicular); candidates.Add(middle - perpendicular);
        }
        var plans = new List<DRCoveragePlan>();
        foreach (var p in candidates.Select(p => p with { Y = player.Y }).Distinct())
        {
            if (!Utilities.NavigationRules.IsFinite(p) || DistanceSquared(p, points[anchor]) > PlanningRadius * PlanningRadius + .1f) continue;
            var count = pending.Count(i => SegmentDistanceSquared(points[i], player, p) <= DetectionRadius * DetectionRadius);
            if (count == 0) continue;
            var seconds = MathF.Sqrt(DistanceSquared(player, p)) / speed + ObservationMs / 1000f;
            var score = seconds / count;
            PatrolCoverageCrystal? selected = null;
            foreach (var crystal in crystals.Where(c => float.IsFinite(c.Position.X) && float.IsFinite(c.Position.Y)))
            {
                var start = new Vector3(crystal.Position.X, player.Y, crystal.Position.Y);
                var viaCount = pending.Count(i => SegmentDistanceSquared(points[i], start, p) <= DetectionRadius * DetectionRadius);
                if (viaCount == 0) continue;
                var viaSeconds = teleportSeconds + MathF.Sqrt(DistanceSquared(start, p)) / speed + 2 * ObservationMs / 1000f;
                var viaScore = viaSeconds / viaCount;
                if (viaScore < score || viaScore == score && viaCount > count)
                { selected = crystal; count = viaCount; seconds = viaSeconds; score = viaScore; }
            }
            plans.Add(new(anchor, p, selected, count, seconds, score));
        }
        return plans.OrderBy(p => p.Score).ThenByDescending(p => p.Coverage)
            .ThenBy(p => p.Destination.X).ThenBy(p => p.Destination.Z).ToArray();
    }
}
