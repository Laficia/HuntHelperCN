using System;
using System.Numerics;
using HuntHelper.Utilities;

namespace HuntHelper.Managers.Automation;

/// <summary>Unchanged DR scanner crystal exclusions; no Ruby Sea height overrides.</summary>
public static class DRScanRules
{
    internal const uint RubySea = 613;
    internal const uint RubySeaFerryNpc = 1024622;
    internal static readonly Vector3 RubySeaCrystal = new(84.6f, 3.03f, -589.19f);
    internal static readonly Vector3 RubySeaUnderwaterSpawn = new(-349, -170, -379);
    internal static readonly Vector3 RubySeaFerryDock = new(102.43f, .28f, -550.96f);
    internal static readonly Vector3 RubySeaFerryLanding = new(562.32f, .94f, 841.25f);
    internal const float RubySeaFerryInteractionRange = 2.7f;

    // AutoMarksFinder.Scanner.cs, commit 292258453537f62e9cd45f4d5c94d33775cc1367.
    public static bool CrystalAllowed(uint id) => id is not (105 or 148 or 173 or 175 or 181 or 201);

    internal static bool IncludesAPatrolPoint(PatrolRoute map, Vector3 position)
    {
        // The underwater spawn near (14.4, 13.9) is still an A-rank location.
        // Skip the dedicated detour while continuing to record any A rank that loads nearby.
        if (map.TerritoryId == RubySea && Anchor(position) == Anchor(RubySeaUnderwaterSpawn)) return false;

        // DR uses world coordinates, while the rank data uses rounded map coordinates.
        // Keep shared points and preserve any unmatched point when the rank is unknown.
        const float matchingRadius = .75f;
        var coordinates = MapHelpers.ConvertToMapCoordinate(position, map.MapScale);
        var hasB = false;
        foreach (var known in map.Points)
        {
            if (!(Vector2.DistanceSquared(known.MapPosition, coordinates) <= matchingRadius * matchingRadius)) continue;
            if (known.ForA) return true;
            hasB |= known.ForB;
        }
        return !hasB;
    }

    internal static bool IsRubySeaSouthPoint(Vector3 position) =>
        Anchor(position) is (286, 506) or (476, 861) or (121, 586);

    internal static bool IsRubySeaEastPoint(Vector3 position) =>
        Anchor(position) is (526, 141) or (226, -724);

    private static (int X, int Z) Anchor(Vector3 position) =>
        ((int)MathF.Round(position.X), (int)MathF.Round(position.Z));
}
