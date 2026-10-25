using HuntHelper.Managers.Hunts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace HuntHelper.Utilities;

public sealed record HuntRelaySpecies(string Rank, uint NameId, string Name, string Alias, uint TerritoryId, int Version);
public sealed record HuntRelayMap(uint TerritoryId, uint MapId, Vector2 Position, float MaximumCoordinate, string MapName = "");
public sealed record HuntRelayResult(string Rank, HuntTrainMob Mob);

/// <summary>Validates structured Sonar-style map announcements; never infers a missing world.</summary>
public static class HuntRelayParser
{
    private static readonly Regex Header = new(@"^\s*(?:\[Sonar[^\]]*\]\s*)?(?:Rank\s*)?(?<rank>[A-Z]+)\s*[:：]\s*(?<name>.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WorldTags = new(@"[<＜](?<world>[^<>＜＞]+)[>＞]", RegexOptions.CultureInvariant);
    private static readonly Regex LineText = new(@"(?:\bi\s*|分线\s*|第\s*)(?<line>[1-9])(?:\s*线)?\b|(?<line>[1-9])\s*线", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static bool TryParse(string beforeLink, string afterLink, HuntRelayMap map,
        IReadOnlyList<HuntRelaySpecies> species, IReadOnlyList<HuntTrainWorld> worlds,
        IReadOnlyDictionary<char, uint> instanceGlyphs, DateTime recordedUtc, out HuntRelayResult? result,
        bool sonarTagged = false)
        => TryParse(beforeLink, afterLink, map, species, worlds, instanceGlyphs, recordedUtc, out result, out _, sonarTagged);

    public static string? RankHint(string text)
    {
        var match = Header.Match(Clean(text));
        return match.Success ? match.Groups["rank"].Value.ToUpperInvariant() : null;
    }

    public static bool TryParse(string beforeLink, string afterLink, HuntRelayMap map,
        IReadOnlyList<HuntRelaySpecies> species, IReadOnlyList<HuntTrainWorld> worlds,
        IReadOnlyDictionary<char, uint> instanceGlyphs, DateTime recordedUtc, out HuntRelayResult? result,
        out string failure, bool sonarTagged = false)
    {
        result = null;
        failure = string.Empty;
        if (map.TerritoryId == 0 || map.MapId == 0 || !float.IsFinite(map.Position.X) || !float.IsFinite(map.Position.Y) ||
            !float.IsFinite(map.MaximumCoordinate) || map.Position.X < 1 || map.Position.Y < 1 ||
            map.Position.X > map.MaximumCoordinate || map.Position.Y > map.MaximumCoordinate)
        { failure = "地图链接或坐标无效"; return false; }
        var prefix = Clean(beforeLink);
        var header = Header.Match(prefix);
        var rank = header.Success ? header.Groups["rank"].Value.ToUpperInvariant() : null;
        if (header.Success && rank is not ("A" or "S")) { failure = "仅支持 A/S 怪，SS及随从不在录制范围"; return false; }
        if (!header.Success && !sonarTagged) { failure = "无法识别 Rank A/S 消息格式"; return false; }
        var name = header.Success ? Clean(header.Groups["name"].Value) : prefix;
        var named = species.Where(s => s.Rank is "A" or "S" && (rank == null || rank == s.Rank) &&
            (header.Success ? Same(name, s.Name) || Same(name, s.Alias)
                : ContainsName(name, s.Name) || ContainsName(name, s.Alias)))
            .DistinctBy(s => s.NameId).ToList();
        if (named.Count == 0) { failure = "怪物名称无法识别，请使用 Sonar 默认定位格式"; return false; }
        var candidates = named.Where(s => s.TerritoryId == 0 || s.TerritoryId == map.TerritoryId).ToList();
        if (candidates.Count == 0) { failure = "怪物与地图不匹配"; return false; }
        if (candidates.Count != 1) { failure = "怪物名称不唯一，无法确认目标"; return false; }
        var worldNames = WorldTags.Matches(afterLink).Select(m => Clean(m.Groups["world"].Value)).Distinct().ToList();
        if (worldNames.Count != 1) { failure = worldNames.Count == 0 ? "缺少明确区服 <服务器>" : "消息包含多个区服，无法确认来源"; return false; }
        var matches = worlds.Where(w => w.WorldID != 0 && Same(w.WorldName, worldNames[0])).DistinctBy(w => w.WorldID).ToList();
        if (matches.Count != 1) { failure = "来源服务器无法识别，请核对服务器名称"; return false; }
        var locationText = WorldTags.Replace(afterLink, string.Empty);
        var lines = locationText.Where(instanceGlyphs.ContainsKey).Select(c => instanceGlyphs[c])
            .Concat(LineText.Matches(locationText).Select(m => uint.Parse(m.Groups["line"].Value)))
            .Where(i => i is >= 1 and <= 9).Distinct().ToArray();
        if (lines.Length > 1) { failure = "分线信息冲突"; return false; }
        var selected = candidates[0]; var world = matches[0];
        result = new(selected.Rank, new HuntTrainMob(selected.Name, selected.NameId, map.TerritoryId, map.MapId,
            lines.FirstOrDefault(), map.MapName, map.Position, recordedUtc, false,
            world.WorldID, world.WorldName, world.DataCenterName, source: HuntRecordSource.Sonar,
            recordedAtUtc: recordedUtc, sonarReceivedUtc: recordedUtc));
        return true;
    }

    private static bool ContainsName(string text, string name) => !string.IsNullOrWhiteSpace(name) &&
        text.Contains(Clean(name), StringComparison.OrdinalIgnoreCase);
    private static bool Same(string a, string b) => string.Equals(Clean(a), Clean(b), StringComparison.OrdinalIgnoreCase);
    private static string Clean(string text) => string.Concat(text.Where(c => !char.IsControl(c) &&
        c is not (>= '\ue000' and <= '\uf8ff'))).Normalize(NormalizationForm.FormKC).Trim();
}
