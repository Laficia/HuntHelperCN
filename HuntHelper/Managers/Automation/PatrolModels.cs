using HuntHelper.Utilities;
using HuntHelper.Managers.Hunts.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

public sealed class PatrolPoint
{
    public bool ForA { get; set; } = true;
    public bool ForB { get; set; } = true;
    public Vector3? Position { get; set; }
    public Vector2 MapPosition { get; set; }
}

public sealed class PatrolRoute
{
    [JsonProperty(Required = Required.Always)]
    public uint TerritoryId { get; set; }
    [JsonProperty(Required = Required.Always)]
    public uint MapId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Expansion { get; set; }
    [JsonProperty(Required = Required.Always)]
    public List<PatrolPoint> Points { get; set; } = new();
}

public static class PatrolRouteRules
{
    public static bool Valid(IReadOnlyList<PatrolRoute> routes) => routes.Count <= 200 &&
        routes.Select(r => r?.TerritoryId).Distinct().Count() == routes.Count &&
        routes.All(r => r != null && r.TerritoryId != 0 && r.MapId != 0 && r.Points != null &&
            r.Points.Count <= 2000 && r.Points.All(p => p != null && (p.Position is { } pos
                ? NavigationRules.IsFinite(pos) && Math.Abs(pos.X) <= 10000 && Math.Abs(pos.Y) <= 10000 && Math.Abs(pos.Z) <= 10000
                : float.IsFinite(p.MapPosition.X) && float.IsFinite(p.MapPosition.Y) && p.MapPosition.X is >= 0 and <= 100 && p.MapPosition.Y is >= 0 and <= 100)));

}

public readonly record struct PatrolContext(uint World, uint Territory, uint Map, uint Instance,
    Vector3 Position, bool LoggedIn, bool Ready, bool InCombat, bool Dead);
public readonly record struct PatrolDetection(uint Id, bool IsA, Vector3? Position = null);
public sealed record PatrolCrystal(uint Id, string Name, Vector2? Position);
public sealed record PatrolTeleport(PatrolCrystal Crystal, Vector2? WorldPosition, float DirectSeconds, float ViaSeconds);

public interface IPatrolHost
{
    string WorldTravelError => "请先安装并启用 Lifestream";
    bool WorldTravelBusy => false;
    string WorldVisitProblem(HuntTrainWorld world) => WorldTravelError;
    bool ChangeWorld(HuntTrainWorld world, out string error) { error = WorldTravelError; return false; }
    void AbortWorldTravel() { }
    PatrolContext Context { get; }
    bool Navigating { get; }
    bool NavigationInterrupted => false;
    string NavigationMessage { get; }
    bool Teleport(PatrolRoute route, out string error);
    bool Teleport(PatrolRoute route, PatrolTeleport? plan, out string error) => Teleport(route, out error);
    PatrolTeleport? PlanTeleport(PatrolRoute route, PatrolPoint point, PatrolOptions options) => null;
    bool ResolvePoint(PatrolRoute route, PatrolPoint point, out Vector3 destination)
    { destination = point.Position ?? default; return point.Position.HasValue; }
    IReadOnlyList<PatrolDetection> KnownDetections(bool rankA, bool rankB) => Array.Empty<PatrolDetection>();
    bool PrepareMount(bool mount, bool takeoff, out string message);
    bool Move(PatrolRoute route, PatrolPoint point, out Vector3 destination, out string error);
    void StopNavigation();
    IReadOnlyList<PatrolDetection> Scan(bool rankA, bool rankB);
}

public sealed class PatrolOptions
{
    public List<HuntTrainWorld> Worlds { get; set; } = new();
    public bool SmartTeleport { get; set; } = true;
    public float TeleportSeconds { get; set; } = 25;
    public bool AdaptiveDetection { get; set; } = true;
    public bool UseRecordedHunts { get; set; } = true;
    public bool RankA { get; set; } = true;
    public bool RankB { get; set; }
    public bool Mount { get; set; } = true;
    public bool Takeoff { get; set; } = true;
    public float Radius { get; set; } = 75;
    public int WaitMs { get; set; } = 5000;
}
