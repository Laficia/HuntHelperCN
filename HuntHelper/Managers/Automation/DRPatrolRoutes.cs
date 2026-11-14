using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers.Automation;

/// <summary>Loads the unchanged, pinned DR spawn-point resource; never substitutes preset routes.</summary>
public static class DRPatrolRoutes
{
    public const int ExpectedMapCount = 47;
    public const int ExpectedPointCount = 924;
    public const string FileName = "DRSpawnPoints.json";
    public const string ResourceCommit = "f516a23d3f14c1d3eeedf342f92c4b99c6853306";

    private static readonly IReadOnlyDictionary<uint, int> PointCounts = new Dictionary<uint, int>
    {
        [134] = 24, [135] = 20, [137] = 32, [138] = 23, [139] = 17, [140] = 22,
        [141] = 23, [145] = 34, [146] = 42, [147] = 17, [148] = 23, [152] = 35,
        [153] = 28, [154] = 20, [155] = 60, [156] = 30, [180] = 23,
        [397] = 24, [398] = 20, [399] = 22, [400] = 22, [401] = 26, [402] = 20,
        [612] = 20, [613] = 14, [614] = 14, [620] = 18, [621] = 19, [622] = 18,
        [813] = 18, [814] = 17, [815] = 18, [816] = 17, [817] = 15, [818] = 17,
        [956] = 10, [957] = 9, [958] = 9, [959] = 10, [960] = 9, [961] = 11,
        [1187] = 9, [1188] = 9, [1189] = 9, [1190] = 9, [1191] = 9, [1192] = 9,
    };

    public static IReadOnlyList<PatrolRoute> Load(string dataFilePath, IReadOnlyList<PatrolRoute> maps)
    {
        if (!TryLoad(dataFilePath, maps, out var routes, out var error))
            throw new InvalidDataException(error);
        return routes;
    }

    public static bool TryLoad(string dataFilePath, IReadOnlyList<PatrolRoute> maps,
        out List<PatrolRoute> routes, out string error)
    {
        routes = new();
        error = string.Empty;
        try
        {
            if (!File.Exists(dataFilePath))
            {
                error = "DR 点位文件缺失，请重新安装完整插件包（Data/DRSpawnPoints.json）";
                return false;
            }
            return TryParse(File.ReadAllText(dataFilePath), maps, out routes, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            error = $"DR 点位文件读取失败：{ex.Message}";
            return false;
        }
    }

    public static bool TryParse(string json, IReadOnlyList<PatrolRoute> maps,
        out List<PatrolRoute> routes, out string error)
    {
        routes = new();
        error = string.Empty;
        try
        {
            using var text = new StringReader(json);
            using var reader = new JsonTextReader(text) { DateParseHandling = DateParseHandling.None };
            var token = JToken.Load(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
            if (reader.Read()) throw new InvalidDataException("文件尾部存在多余内容");
            if (token is not JObject root || root.Count != ExpectedMapCount)
                throw new InvalidDataException($"应包含 {ExpectedMapCount} 张地图");
            if (maps == null || maps.Any(map => map == null || map.TerritoryId == 0 || map.MapId == 0) ||
                maps.Select(map => map.TerritoryId).Distinct().Count() != maps.Count)
                throw new InvalidDataException("游戏地图资料无效或重复，无法匹配 DR 点位");

            var metadata = maps.ToDictionary(map => map.TerritoryId);
            var loaded = new List<PatrolRoute>();
            var count = 0;
            foreach (var entry in root.Properties())
            {
                if (!uint.TryParse(entry.Name, out var territory) || entry.Name != territory.ToString() ||
                    !PointCounts.TryGetValue(territory, out var expected))
                    throw new InvalidDataException($"存在未知地图：{entry.Name}");
                if (!metadata.TryGetValue(territory, out var map))
                    throw new InvalidDataException($"游戏地图资料缺少地图 {territory}，请检查国服数据是否已加载");
                if (entry.Value is not JArray points || points.Count != expected)
                    throw new InvalidDataException($"地图 {map.Name}（{territory}）应包含 {expected} 个点位");
                var route = new PatrolRoute
                {
                    TerritoryId = map.TerritoryId, MapId = map.MapId,
                    Name = map.Name, Expansion = map.Expansion,
                };
                var seen = new HashSet<Vector3>();
                foreach (var item in points)
                {
                    var location = $"地图 {map.Name}（{territory}）第 {route.Points.Count + 1} 点";
                    if (item is not JObject point || point["Zone"]?.Type != JTokenType.Integer ||
                        !uint.TryParse(point["Zone"]!.ToString(), out var zone) || zone != territory)
                        throw new InvalidDataException($"{location} 的 Zone 与地图不一致");
                    if (point["Position"] is not JObject position)
                        throw new InvalidDataException($"{location} 缺少三维 Position");
                    var vector = new Vector3(Coordinate(position, "X", location), Coordinate(position, "Y", location),
                        Coordinate(position, "Z", location));
                    if (!seen.Add(vector)) throw new InvalidDataException($"{location} 与本地图其它点重复");
                    route.Points.Add(new PatrolPoint { Position = vector, ForA = true, ForB = false });
                }
                count += route.Points.Count;
                loaded.Add(route);
            }
            if (count != ExpectedPointCount)
                throw new InvalidDataException($"应包含 {ExpectedPointCount} 个点位，实际 {count} 个");
            routes = loaded;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or ArgumentException or OverflowException or FormatException or InvalidCastException)
        {
            error = $"DR 点位数据校验失败：{ex.Message}；不会改用预设路线，请重新安装完整插件包";
            return false;
        }
    }

    private static float Coordinate(JObject position, string axis, string location)
    {
        var token = position[axis];
        if (token?.Type is not (JTokenType.Integer or JTokenType.Float))
            throw new InvalidDataException($"{location} 的 {axis} 坐标缺失或不是数值");
        var value = token.Value<double>();
        if (!double.IsFinite(value) || Math.Abs(value) > 10000)
            throw new InvalidDataException($"{location} 的 {axis} 坐标不是有限有效坐标");
        return (float)value;
    }
}
