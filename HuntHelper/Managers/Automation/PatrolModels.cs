using HuntHelper.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

public sealed class PatrolPoint
{
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

// Geometry can be shared between worlds. Hunt results and running sessions cannot.
public sealed class PatrolRouteStore
{
    private readonly string _path;
    public List<PatrolRoute> Routes { get; private set; } = new();
    public string? Error { get; private set; }
    private bool _writable = true;
    public PatrolRouteStore(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;
        try
        {
            var routes = JsonConvert.DeserializeObject<List<PatrolRoute>>(File.ReadAllText(path));
            if (routes == null || !Valid(routes)) throw new InvalidDataException("路线文件格式或坐标无效");
            Routes = routes;
        }
        catch (Exception ex) { Error = ex.Message; _writable = false; }
    }

    public static bool Valid(IReadOnlyList<PatrolRoute> routes) => routes.Count <= 200 &&
        routes.Select(r => r?.TerritoryId).Distinct().Count() == routes.Count &&
        routes.All(r => r != null && r.TerritoryId != 0 && r.MapId != 0 && r.Points != null &&
            r.Points.Count <= 2000 && r.Points.All(p => p != null && (p.Position is { } pos
                ? NavigationRules.IsFinite(pos) && Math.Abs(pos.X) <= 10000 && Math.Abs(pos.Y) <= 10000 && Math.Abs(pos.Z) <= 10000
                : float.IsFinite(p.MapPosition.X) && float.IsFinite(p.MapPosition.Y) && p.MapPosition.X is >= 0 and <= 100 && p.MapPosition.Y is >= 0 and <= 100)));

    public bool Save()
    {
        if (!_writable) return false;
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            if (!Valid(Routes)) throw new InvalidDataException("路线数据无效");
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(temporary, JsonConvert.SerializeObject(Routes, Formatting.Indented));
            File.Move(temporary, _path, true);
            Error = null;
            return true;
        }
        catch (Exception ex) { Error = ex.Message; return false; }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public bool Import(string code)
    {
        var imported = ExportImport.Import<List<PatrolRoute>?>(code, null);
        if (!_writable || imported == null || !Valid(imported)) { Error = "不是有效的 HHT 巡点路线代码"; return false; }
        var previous = Routes;
        Routes = imported;
        if (Save()) return true;
        Routes = previous;
        return false;
    }

    public void Put(PatrolRoute route)
    {
        Routes.RemoveAll(r => r.TerritoryId == route.TerritoryId);
        Routes.Add(route);
    }

    public static void Sort(PatrolRoute route)
    {
        if (route.Points.Count < 2 || route.Points.Any(p => !p.Position.HasValue)) return;
        var remaining = route.Points.Skip(1).ToList();
        var ordered = new List<PatrolPoint> { route.Points[0] };
        while (remaining.Count > 0)
        {
            var next = remaining.MinBy(p => Vector3.DistanceSquared(ordered[^1].Position!.Value, p.Position!.Value))!;
            ordered.Add(next); remaining.Remove(next);
        }
        route.Points = ordered;
    }
}

public readonly record struct PatrolContext(uint World, uint Territory, uint Map, uint Instance,
    Vector3 Position, bool LoggedIn, bool Ready, bool InCombat, bool Dead);
public readonly record struct PatrolDetection(uint Id, bool IsA);

public interface IPatrolHost
{
    PatrolContext Context { get; }
    bool Navigating { get; }
    string NavigationMessage { get; }
    bool Teleport(PatrolRoute route, out string error);
    bool PrepareMount(bool mount, bool takeoff, out string message);
    bool Move(PatrolRoute route, PatrolPoint point, out Vector3 destination, out string error);
    void StopNavigation();
    IReadOnlyList<PatrolDetection> Scan(bool rankA, bool rankB);
}

public sealed class PatrolOptions
{
    public bool RankA { get; set; } = true;
    public bool RankB { get; set; }
    public bool Mount { get; set; } = true;
    public bool Takeoff { get; set; } = true;
    public float Radius { get; set; } = 75;
    public int WaitMs { get; set; } = 5000;
}
