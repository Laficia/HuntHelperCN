using HuntHelper.Managers.Hunts.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace HuntHelper.Utilities;

public sealed record HuntMacroPart(string Text, bool IsFlag = false);

/// <summary>Pure template expansion. Values come from the clicked record, never the player's map flag.</summary>
public static class HuntPointMacro
{
    private static readonly Regex Tokens = new(@"<flag>|<pos>|<服务器>|<world>|<名字>|<name>|<等级>|<rank>|<地图>|<map>|<分线>|<instance>|<x>|<y>", RegexOptions.IgnoreCase);
    private static readonly Regex Channel = new(@"^/(?:p|party|sh|shout|s|say|y|yell|a|alliance|fc|freecompany|e|echo|l[1-8]?|linkshell[1-8]?|cwl[1-8]?|cwlinkshell[1-8]?)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<IReadOnlyList<HuntMacroPart>> Expand(string template, HuntTrainMob mob, string rank, uint currentWorld)
    {
        if (string.IsNullOrWhiteSpace(template)) throw new ArgumentException("请先保存坐标宏");
        if (mob.WorldID == 0 || string.IsNullOrWhiteSpace(mob.WorldName) || mob.TerritoryID == 0 || mob.MapID == 0 ||
            !float.IsFinite(mob.Position.X) || !float.IsFinite(mob.Position.Y)) throw new ArgumentException("记录的服务器或点位无效");
        var lines = template.Replace("\r\n", "\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length > 15) throw new ArgumentException("坐标宏最多 15 行");
        var result = new List<IReadOnlyList<HuntMacroPart>>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (!Channel.IsMatch(line) || line.Any(c => char.IsControl(c)))
                throw new ArgumentException("每行须以聊天频道指令开头，例如 /p、/sh 或 /echo；不支持技能、传送或等待指令");
            var parts = new List<HuntMacroPart>();
            var start = 0;
            foreach (Match token in Tokens.Matches(line))
            {
                if (token.Index > start) parts.Add(new(line[start..token.Index]));
                var key = token.Value.ToLowerInvariant();
                var flag = key is "<flag>" or "<pos>";
                var value = key switch
                {
                    "<服务器>" or "<world>" => mob.WorldID == currentWorld ? "本地" : mob.WorldName,
                    "<名字>" or "<name>" => mob.Name,
                    "<等级>" or "<rank>" => rank,
                    "<地图>" or "<map>" => mob.MapName,
                    "<分线>" or "<instance>" => mob.Instance == 0 ? "" : $"{mob.Instance}线",
                    "<x>" => mob.Position.X.ToString("0.0", CultureInfo.InvariantCulture),
                    "<y>" => mob.Position.Y.ToString("0.0", CultureInfo.InvariantCulture),
                    _ => ""
                };
                if (value.Any(char.IsControl)) throw new ArgumentException("记录包含无效控制字符");
                parts.Add(new(value, flag));
                start = token.Index + token.Length;
            }
            if (start < line.Length) parts.Add(new(line[start..]));
            result.Add(parts);
        }
        if (result.Count == 0) throw new ArgumentException("坐标宏不能为空");
        return result;
    }
}
