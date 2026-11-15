using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace LuckyStar;

public sealed record SSpawnWatch(long RunId,FarmScope Scope,uint MapId,uint NameId,DateTimeOffset StartedAt);
public readonly record struct SSpawnSignal(long RunId,FarmScope Scope,uint NameId,DateTimeOffset ObservedAt,string Source);
public sealed record SSpawnChat(string Sender,string BeforeLink,string AfterLink,int PrimaryLineMapLinks,uint TerritoryId,uint MapId,uint MapTerritoryId);

/// <summary>Explicit identity only: never fill in a missing server or instance from the player.</summary>
public static class SSpawnStopRules
{
    public static bool SupportedTarget(uint territory,uint nameId)
    {
        var index=Array.IndexOf(FarmProfiles.Territories,territory);
        return index>=0 && FarmProfiles.SRankNameIds[index]==nameId;
    }
    private static readonly Regex Header=new(@"^\s*(?:Rank\s+)?S\s*[:：]\s*(?<name>.+?)\s*$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex Worlds=new(@"[<＜](?<world>[^<>＜＞]+)[>＞]",RegexOptions.CultureInvariant);
    private static readonly Regex Lines=new(@"\bi\s*(?<line>[0-9])\b|(?:分线\s*|第\s*)(?<line>[0-9])\s*线?|(?<line>[0-9])\s*线",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    private static readonly Regex Dead=new(@"\b(?:dead|killed|history|expired)\b|击杀|死亡|讨伐|消失|历史|过期",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public static string Clean(string value)=>string.Concat(value.Normalize(NormalizationForm.FormKC).Where(c=>!char.IsControl(c) && c is not (>= '\ue000' and <= '\uf8ff'))).Trim();

    public static bool ShouldStop(SSpawnWatch? watch,SSpawnSignal signal,FarmContext current,DateTimeOffset now)
        => watch!=null && watch.RunId>0 && watch.MapId!=0 && watch.Scope.World!=0 && SupportedTarget(watch.Scope.Territory,watch.NameId) &&
           current.Ready && current.LoggedIn && !current.Loading && current.Scope==watch.Scope &&
           signal.RunId==watch.RunId && signal.Scope==watch.Scope && signal.NameId==watch.NameId &&
           signal.ObservedAt>=watch.StartedAt && signal.ObservedAt<=now.AddSeconds(1) && now-signal.ObservedAt<=TimeSpan.FromSeconds(30);

    public static bool TryParseSonar(SSpawnWatch watch,SSpawnChat chat,IReadOnlyCollection<string> monsterNames,
        Func<string,uint?> resolveWorld,DateTimeOffset receivedAt,out SSpawnSignal signal)
    {
        signal=default;
        if(!string.Equals(chat.Sender.Trim(),"Sonar",StringComparison.OrdinalIgnoreCase) || chat.PrimaryLineMapLinks!=1 ||
           chat.TerritoryId!=watch.Scope.Territory || chat.MapId==0 || chat.MapId!=watch.MapId || chat.MapTerritoryId!=chat.TerritoryId ||
           chat.BeforeLink.Contains('\n') || chat.BeforeLink.Contains('\r'))return false;
        var header=Header.Match(Clean(chat.BeforeLink));
        if(!header.Success || !monsterNames.Any(name=>string.Equals(Clean(name),Clean(header.Groups["name"].Value),StringComparison.OrdinalIgnoreCase)))return false;
        // Other plugins may append teleport/map links on following lines.
        var after=chat.AfterLink.Split(['\r','\n'],2)[0];
        if(Dead.IsMatch(Clean(after)))return false;
        var tags=Worlds.Matches(Clean(after));
        if(tags.Count!=1)return false;
        var world=resolveWorld(Clean(tags[0].Groups["world"].Value));
        if(world is null or 0)return false;
        var instanceText=Worlds.Replace(after,"");
        var instances=instanceText.Where(c=>c is >= '\ue0b1' and <= '\ue0b9').Select(c=>(uint)(c-'\ue0b0'))
            .Concat(Lines.Matches(Clean(instanceText)).Select(m=>uint.Parse(m.Groups["line"].Value))).Distinct().ToArray();
        if(instances.Length>1)return false;
        signal=new(watch.RunId,new(world.Value,chat.TerritoryId,instances.FirstOrDefault()),watch.NameId,receivedAt,"Sonar刷新播报");
        return true;
    }
}
