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
        var candidates = crystals.Where(c => c.Id != 0).OrderBy(c => c.Id).ToList();
        if (candidates.Count == 0) return null;
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
                ? Horizontal(NavigationRules.FromHuntMapCoordinates(map, scale)) : null;
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
