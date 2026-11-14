using HuntHelper.Managers.Hunts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace HuntHelper.Utilities;

public sealed record HuntMacroPart(string Text, bool IsFlag = false, int WaitSeconds = 0);

/// <summary>Pure template expansion. Values come from the clicked record, never the player's map flag.</summary>
public static class HuntPointMacro
{
    private static readonly Regex Tokens = new(@"<flag>|<pos>|<服务器>|<sever>|<world>|<名字>|<name>|<等级>|<lv>|<rank>|<地图>|<map>|<分线>|<ins>|<instance>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex RemovedCoordinates = new(@"<x>|<y>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Channel = new(@"^/(?:p|party|sh|shout|s|say|y|yell|a|alliance|fc|freecompany|e|echo|l[1-8]?|linkshell[1-8]?|cwl[1-8]?|cwlinkshell[1-8]?)\s+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex WaitMarkers = new(@"<\s*wait[^>]*(?:>|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ValidWait = new(@"^<wait\.([0-9]+)>$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static bool ContainsWaitMarker(string text) => WaitMarkers.IsMatch(text);

    public static IReadOnlyList<IReadOnlyList<HuntMacroPart>> Expand(string template, HuntTrainMob? mob, string rank, uint currentWorld)
    {
        if (string.IsNullOrWhiteSpace(template)) throw new ArgumentException("请先保存坐标宏");
        if (RemovedCoordinates.IsMatch(template)) throw new ArgumentException("已取消 <x>、<y> 变量，请改用 <flag> 点位链接");
        if (mob == null && Tokens.IsMatch(template)) throw new ArgumentException("请先在来源 HHT / HHS 窗口选中怪物记录，再发送含点位或怪物变量的宏");
        if (mob != null && (mob.WorldID == 0 || string.IsNullOrWhiteSpace(mob.WorldName) || mob.TerritoryID == 0 || mob.MapID == 0 ||
            !float.IsFinite(mob.Position.X) || !float.IsFinite(mob.Position.Y))) throw new ArgumentException("记录的服务器或点位无效");
        var lines = template.Replace("\r\n", "\n").Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).ToArray();
        if (lines.Length > 15) throw new ArgumentException("坐标宏最多 15 行");
        var result = new List<IReadOnlyList<HuntMacroPart>>();
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Any(char.IsControl)) throw new ArgumentException("宏中含有无效控制字符");
            var waits = WaitMarkers.Matches(line);
            if (waits.Count > 1) throw new ArgumentException("每行最多一个 <wait.数字>，需要连续等待请另起一行");
            var waitSeconds = 0;
            if (waits.Count == 1)
            {
                var marker = waits[0];
                var valid = ValidWait.Match(marker.Value);
                if (!valid.Success) throw new ArgumentException("等待标记须写为 <wait.正整数>，例如 <wait.1>；不支持零、负数或小数");
                var digits = valid.Groups[1].Value.TrimStart('0');
                if (digits.Length == 0) throw new ArgumentException("等待秒数须大于零，例如 <wait.1>");
                // Native macro waits clamp values above 60; avoid integer overflow on long input.
                waitSeconds = digits.Length > 2 ? 60 : Math.Min(int.Parse(digits), 60);
                line = line.Remove(marker.Index, marker.Length).Trim();
            }
            if (line.Length > 0 && !Channel.IsMatch(line))
                throw new ArgumentException("每行须以 /p、/sh、/echo 等聊天频道开头，或只写 <wait.数字>；不支持技能、传送等指令");
            var parts = new List<HuntMacroPart>();
            var start = 0;
            foreach (Match token in Tokens.Matches(line))
            {
                var target = mob!; // A token-bearing template requires a valid record above.
                if (token.Index > start) parts.Add(new(line[start..token.Index]));
                var key = token.Value.ToLowerInvariant();
                var flag = key is "<flag>" or "<pos>";
                var value = key switch
                {
                    "<服务器>" or "<sever>" or "<world>" => target.WorldID == currentWorld ? "本地" : target.WorldName,
                    "<名字>" or "<name>" => target.Name,
                    "<等级>" or "<lv>" or "<rank>" => rank,
                    "<地图>" or "<map>" => target.MapName,
                    "<分线>" or "<ins>" or "<instance>" => target.Instance == 0 ? "" : $"{target.Instance}线",
                    _ => ""
                };
                if (value.Any(char.IsControl)) throw new ArgumentException("记录包含无效控制字符");
                parts.Add(new(value, flag));
                start = token.Index + token.Length;
            }
            if (start < line.Length) parts.Add(new(line[start..]));
            if (waitSeconds > 0) parts.Add(new("", WaitSeconds: waitSeconds));
            result.Add(parts);
        }
        if (result.Count == 0) throw new ArgumentException("坐标宏不能为空");
        return result;
    }
}
