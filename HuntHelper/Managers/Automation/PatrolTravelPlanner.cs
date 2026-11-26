using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

/// <summary>Conservative travel-time estimate; a teleport always means an unlocked game crystal.</summary>
public static class PatrolTravelPlanner
{
    public static Vector2 Horizontal(Vector3 p) => new(p.X, p.Z);
    public static Vector3 FlightTarget(uint territory, Vector3 ground, bool flying) =>
        territory == 1190 && flying ? ground + new Vector3(0, 20, 0) : ground;

    internal static uint NearestHuntCrystal(HuntTrainMob mob, IReadOnlyList<PatrolCrystal> crystals) =>
        crystals.OrderBy(c => c.Id).MinBy(crystal =>
        {
            if (crystal.WorldPosition is { } position && mob.WorldPosition is { } target && NavigationRules.IsFinite(target))
                return Vector3.DistanceSquared(position, target);
            // Legacy records can contain only map XY. One HHT map unit is 50 world units.
            return crystal.Position is { } mapPosition
                ? Vector2.DistanceSquared(mapPosition, mob.Position) * 2500f
                : float.PositiveInfinity;
        })?.Id ?? 0;

    public static Vector2? Point(PatrolPoint point, float scale)
    {
        if (point.Position is { } p && NavigationRules.IsFinite(p)) return Horizontal(p);
        if (scale <= 0 || !float.IsFinite(scale) || !float.IsFinite(point.MapPosition.X) || !float.IsFinite(point.MapPosition.Y)) return null;
        return Horizontal(NavigationRules.FromHuntMapCoordinates(point.MapPosition, scale));
    }

    public static PatrolTeleport? Select(PatrolContext context, PatrolRoute route, PatrolPoint point, PatrolOptions options,
        IEnumerable<PatrolCrystal> crystals, float scale, float speed)
    {
        var entering = context.Territory != route.TerritoryId || context.Map != route.MapId;
        if (!entering && !options.SmartTeleport) return null;
        var target = Point(point, scale);
        var candidates = crystals.Where(c => c.Id != 0 &&
            (options.RouteMode != PatrolRouteMode.DRCoverage || DRScanRules.CrystalAllowed(c.Id))).OrderBy(c => c.Id).ToList();
        if (candidates.Count == 0) return null;
        if (entering && options.RouteMode == PatrolRouteMode.DRCoverage)
        {
            // DR enters through the crystal nearest to any spawn on the selected map.
            // The first serialized spawn is not a designated entry point.
            var spawns = route.Points.Select(p => Point(p, scale)).Where(p => p.HasValue).Select(p => p!.Value).ToArray();
            if (spawns.Length == 0 && target.HasValue) spawns = [target.Value];
            if (spawns.Length == 0) return null;
            var entry = candidates.Where(c => c.Position.HasValue && scale > 0)
                .Select(c => new PatrolCoverageCrystal(c, Horizontal(c.WorldPosition ??
                    NavigationRules.FromHuntMapCoordinates(c.Position!.Value, scale))))
                .Where(c => float.IsFinite(c.Position.X) && float.IsFinite(c.Position.Y))
                .MinBy(c => spawns.Min(p => Vector2.DistanceSquared(p, c.Position)));
            return entry != null
                ? new(entry.Crystal, entry.Position, float.PositiveInfinity, 10) : null;
        }
        speed = float.IsFinite(speed) ? Math.Clamp(speed, 6, 20) : 20;
        var overhead = float.IsFinite(options.TeleportSeconds) ? Math.Clamp(options.TeleportSeconds, 10, 120) : 25;
        var radius = Math.Clamp(options.Radius, 10, 150);
        var direct = entering || !target.HasValue ? float.PositiveInfinity :
            Math.Max(0, Vector2.Distance(Horizontal(context.Position), target.Value) - radius) / speed;
        PatrolTeleport? best = null;
        foreach (var crystal in candidates)
        {
            Vector2? position = crystal.Position is { } map && scale > 0 && float.IsFinite(scale) &&
                float.IsFinite(map.X) && float.IsFinite(map.Y) && map.X > 1 && map.Y > 1
                ? Horizontal(crystal.WorldPosition ?? NavigationRules.FromHuntMapCoordinates(map, scale)) : null;
            if (!target.HasValue || !position.HasValue) continue;
            var via = overhead + Math.Max(0, Vector2.Distance(position.Value, target.Value) - radius) / speed;
            if (best == null || via < best.ViaSeconds) best = new(crystal, position, direct, via);
        }
        // Unknown crystal geometry is only a cross-map fallback, never a same-map shortcut.
        if (best == null) return entering ? new(candidates[0], null, direct, overhead) : null;
        return entering || direct - best.ViaSeconds >= Math.Max(10, direct * .2f) ? best : null;
    }

    public static bool RecentLivingScan(HuntTrainMob mob, PatrolContext c, DateTime now) =>
        c.World != 0 && mob.WorldID == c.World && mob.TerritoryID == c.Territory && mob.MapID == c.Map && mob.Instance == c.Instance &&
        !mob.Dead && mob.Source == HuntRecordSource.Scan && mob.LastObservedUtc is { } seen &&
        seen <= now && now - seen <= TimeSpan.FromMinutes(60);
}
