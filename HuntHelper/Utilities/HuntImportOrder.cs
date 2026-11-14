using HuntHelper.Managers.Hunts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Utilities;

/// <summary>Shortest open route by map distance, independently for each world/map/instance.</summary>
public static class HuntImportOrder
{
    public static IReadOnlyList<HuntTrainMob> Optimize(IReadOnlyList<HuntTrainMob> records,
        Func<HuntTrainMob, bool> eligible, Func<HuntTrainMob, IReadOnlyList<Vector2>> entryPoints,
        ISet<(uint World, uint Territory, uint Map, uint Instance)>? affected = null)
    {
        var result = records.ToArray();
        foreach (var group in records.Select((m, i) => (Mob: m, Index: i)).Where(x => eligible(x.Mob))
            .GroupBy(x => (x.Mob.WorldID, x.Mob.TerritoryID, x.Mob.MapID, x.Mob.Instance)))
        {
            var slots = group.ToArray();
            if (slots.Length < 2 || slots.Length > 128 || group.Key.WorldID == 0 || group.Key.MapID == 0 || affected != null && !affected.Contains(group.Key) ||
                slots.Any(x => !float.IsFinite(x.Mob.Position.X) || !float.IsFinite(x.Mob.Position.Y))) continue;
            var points = slots.Select(x => x.Mob.Position).ToArray();
            var entries = entryPoints(slots[0].Mob).Where(p => float.IsFinite(p.X) && float.IsFinite(p.Y)).ToArray();
            var starts = points.Select(p => entries.Length == 0 ? 0 : entries.Min(e => Vector2.Distance(e, p))).ToArray();
            var order = points.Length <= 12 ? Exact(points, starts) : Greedy(points, starts);
            float Cost(IReadOnlyList<int> route) => starts[route[0]] + Enumerable.Range(1, route.Count - 1).Sum(i => Vector2.Distance(points[route[i - 1]], points[route[i]]));
            if (Cost(order) + .00001f >= Cost(Enumerable.Range(0, points.Length).ToArray())) continue;
            for (var i = 0; i < slots.Length; i++) result[slots[i].Index] = slots[order[i]].Mob;
        }
        return result;
    }

    private static int[] Exact(Vector2[] points, float[] starts)
    {
        var n = points.Length; var size = 1 << n;
        var cost = new float[size, n]; var parent = new int[size, n];
        for (var mask = 0; mask < size; mask++) for (var i = 0; i < n; i++) { cost[mask, i] = float.PositiveInfinity; parent[mask, i] = -1; }
        for (var i = 0; i < n; i++) cost[1 << i, i] = starts[i];
        for (var mask = 1; mask < size; mask++)
            for (var last = 0; last < n; last++)
            {
                if (!float.IsFinite(cost[mask, last])) continue;
                for (var next = 0; next < n; next++)
                {
                    if ((mask & (1 << next)) != 0) continue;
                    var combined = mask | (1 << next);
                    var candidate = cost[mask, last] + Vector2.Distance(points[last], points[next]);
                    if (candidate + .00001f >= cost[combined, next]) continue;
                    cost[combined, next] = candidate; parent[combined, next] = last;
                }
            }
        var end = Enumerable.Range(0, n).OrderBy(i => cost[size - 1, i]).First();
        var route = new int[n]; var remaining = size - 1;
        for (var i = n - 1; i >= 0; i--)
        { route[i] = end; var previous = parent[remaining, end]; remaining ^= 1 << end; end = previous; }
        return route;
    }

    private static int[] Greedy(Vector2[] points, float[] starts)
    {
        var remaining = Enumerable.Range(0, points.Length).ToList(); var route = new List<int>();
        while (remaining.Count > 0)
        {
            var next = remaining.OrderBy(i => route.Count == 0 ? starts[i] : Vector2.Distance(points[route[^1]], points[i])).First();
            route.Add(next); remaining.Remove(next);
        }
        // Bounded improvement for unusually large custom imports.
        float Cost(IReadOnlyList<int> r) => starts[r[0]] + Enumerable.Range(1, r.Count - 1).Sum(i => Vector2.Distance(points[r[i - 1]], points[r[i]]));
        var best = Cost(route);
        for (var pass = 0; pass < 4; pass++)
            for (var i = 0; i < route.Count - 1; i++) for (var j = i + 1; j < route.Count; j++)
            { route.Reverse(i, j - i + 1); var c = Cost(route); if (c + .00001f < best) best = c; else route.Reverse(i, j - i + 1); }
        return route.ToArray();
    }
}
