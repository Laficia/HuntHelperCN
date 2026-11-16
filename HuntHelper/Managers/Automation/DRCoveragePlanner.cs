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
    // Official AutoMarksFinder scanner, pinned at 29225845 (2026-11-16).
    public const int ObservationMs = 250;

    public static float DistanceSquared(Vector3 a, Vector3 b) =>
        Vector2.DistanceSquared(new(a.X, a.Z), new(b.X, b.Z));

    public static float SegmentDistanceSquared(Vector3 p, Vector3 a, Vector3 b)
    {
        var line = new Vector2(b.X - a.X, b.Z - a.Z);
        var offset = new Vector2(p.X - a.X, p.Z - a.Z);
        var t = line.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(offset, line) / line.LengthSquared(), 0, 1) : 0;
        return (offset - t * line).LengthSquared();
    }

    public static IEnumerable<DRCoveragePlan?> Plan(Vector3 player, IReadOnlyList<Vector3> points,
        IReadOnlyCollection<int> pending, IReadOnlyList<PatrolCoverageCrystal> crystals,
        Func<Vector3, Vector3?> resolve)
    {
        if (pending.Count == 0) yield break;
        const float speed = 12, teleportSeconds = 10;
        var anchor = pending.OrderBy(i => DistanceSquared(points[i], player)).ThenBy(i => i).First();
        var nearby = pending.Where(i => DistanceSquared(points[i], points[anchor]) <= 4 * PlanningRadius * PlanningRadius)
            .OrderBy(i => i).Select(i => points[i]).ToArray();
        var candidates = new List<Vector3>(nearby);
        foreach (var p in nearby)
        {
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
            var perpendicular = new Vector3(a.Z - b.Z, 0, b.X - a.X) * MathF.Sqrt((PlanningRadius * PlanningRadius - d2 / 4) / d2);
            candidates.Add(middle); candidates.Add(middle + perpendicular); candidates.Add(middle - perpendicular);
        }
        foreach (var candidate in candidates)
        {
            // DR scores the actual voxel result, not the unprojected candidate.
            var resolved = resolve(candidate with { Y = player.Y });
            if (resolved is not { } p || !Utilities.NavigationRules.IsFinite(p) ||
                DistanceSquared(p, points[anchor]) > PlanningRadius * PlanningRadius)
            { yield return null; continue; }
            var count = pending.Count(i => SegmentDistanceSquared(points[i], player, p) <= DetectionRadius * DetectionRadius);
            if (count == 0) { yield return null; continue; }
            var seconds = MathF.Sqrt(DistanceSquared(player, p)) / speed + ObservationMs / 1000f;
            var score = seconds / count;
            PatrolCoverageCrystal? selected = null;
            var crystal = crystals.Where(c => DRScanRules.CrystalAllowed(c.Crystal.Id) && float.IsFinite(c.Position.X) && float.IsFinite(c.Position.Y))
                .MinBy(c => Vector2.DistanceSquared(c.Position, new(p.X, p.Z)));
            if (crystal != null)
            {
                var start = new Vector3(crystal.Position.X, player.Y, crystal.Position.Y);
                var viaCount = pending.Count(i => SegmentDistanceSquared(points[i], start, p) <= DetectionRadius * DetectionRadius);
                if (viaCount == 0) { yield return new(anchor, p, null, count, seconds, score); continue; }
                var viaSeconds = teleportSeconds + MathF.Sqrt(DistanceSquared(start, p)) / speed + 2 * ObservationMs / 1000f;
                var viaScore = viaSeconds / viaCount;
                if (viaScore < score || viaScore == score && viaCount > count)
                { selected = crystal; count = viaCount; seconds = viaSeconds; score = viaScore; }
            }
            yield return new(anchor, p, selected, count, seconds, score);
        }
    }

    public static IEnumerable<Vector3> RetryTargets(Vector3 target, Vector3 anchor)
    {
        yield return target;
        yield return target with { Y = 1024 };
        for (var ring = 1; ring <= 2; ring++)
        for (var direction = 0; direction < 8; direction++)
        {
            var angle = MathF.Tau * direction / 8;
            var nearby = target + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * (ring * 80);
            if (DistanceSquared(nearby, anchor) <= PlanningRadius * PlanningRadius) yield return nearby;
        }
        yield return target with { Y = -1024 };
    }
}
