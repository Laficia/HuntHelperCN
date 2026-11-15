using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.DalamudServices;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace LuckyStar;

/// <summary>Consumes live local observations and Sonar's displayed announcements, without reading its private state.</summary>
public sealed class SSpawnMonitor : IDisposable
{
    private sealed record WatchState(SSpawnWatch Watch,string[] Names,string DisplayName,string WorldName,string MapName);
    private readonly ConcurrentQueue<SSpawnSignal> pending=new();
    private readonly IReadOnlyDictionary<string,uint[]> worlds;
    private WatchState? active;
    private long sequence,nextLocalCheck;
    public string LastStop {get;private set;}="";
    public object Diagnostics
    {
        get {var state=Volatile.Read(ref active);return new{Armed=state!=null,Target=state?.Watch,World=state?.WorldName,Map=state?.MapName,Monster=state?.DisplayName,LastStop,Sources="本地已加载对象／Sonar游戏聊天播报"};}
    }
    public SSpawnMonitor()
    {
        // CN active worlds can have IsPublic=false. Resolve explicit names to unique IDs instead.
        worlds=Svc.Data.GetExcelSheet<World>().Where(w=>w.RowId!=0 && w.DataCenter.RowId!=0 && !string.IsNullOrWhiteSpace(w.Name.ToString()))
            .GroupBy(w=>SSpawnStopRules.Clean(w.Name.ToString()),StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g=>g.Key,g=>g.Select(w=>w.RowId).Distinct().ToArray(),StringComparer.OrdinalIgnoreCase);
        Svc.Chat.ChatMessage+=OnChat;
    }
    public void Arm(FarmProfile profile,FarmContext context)
    {
        Disarm();
        if(!SSpawnStopRules.SupportedTarget(profile.Territory,profile.StopOnName))
            throw new InvalidOperationException("只支持现有七个农怪项目对应的 S 怪");
        if(!context.Ready || !context.LoggedIn || context.Loading || context.Scope.World==0 || context.Scope.Territory!=profile.Territory)
            throw new InvalidOperationException("无法确认农怪服务器与地图，请在对应地图加载完成后开始");
        var territory=Svc.Data.GetExcelSheet<TerritoryType>().GetRow(profile.Territory);
        var notorious=territory.NotoriousMonsterTerritory.ValueNullable;
        var sNames=notorious?.NotoriousMonsters.Select(r=>r.ValueNullable).Where(r=>r.HasValue && r.Value.Rank==3)
            .Select(r=>r!.Value.BNpcName.RowId).Where(id=>id!=0).Distinct().ToArray() ?? [];
        var primary=profile.StopOnName;
        if(primary==0 || !sNames.Contains(primary) || territory.Map.RowId==0)throw new InvalidOperationException("当前农怪项目对应的 S 怪与地图资料不匹配，未开始农怪");
        var name=Svc.Data.GetExcelSheet<BNpcName>().GetRow(primary).Singular.ToString();
        var names=new List<string>{name};
        // These official English species names also appear when Sonar uses English output.
        var english=primary switch{5984=>"Okina",10618=>"Sphatika",10620=>"Ruminator",8890=>"Ixtab",5987=>"Udumbara",4380=>"Leucrotta",2961=>"Minhocao",_=>""};
        if(english.Length>0)names.Add(english);
        var watch=new SSpawnWatch(Interlocked.Increment(ref sequence),context.Scope,territory.Map.RowId,primary,DateTimeOffset.UtcNow);
        var worldName=Svc.Data.GetExcelSheet<World>().GetRow(context.Scope.World).Name.ToString();
        Volatile.Write(ref active,new(watch,names.ToArray(),name,worldName,territory.PlaceName.Value.Name.ToString()));
        LastStop="";nextLocalCheck=0;
    }
    public void Disarm(){Volatile.Write(ref active,null);while(pending.TryDequeue(out _)){};}
    public string? Check(FarmContext context,Func<IReadOnlyList<FarmMob>> mobs)
    {
        var state=Volatile.Read(ref active);
        if(state==null)return null;
        // Retain arrivals during a cave fade and confirm identity after it finishes.
        if(!context.Ready || !context.LoggedIn || context.Loading || context.Scope!=state.Watch.Scope)return null;
        var now=DateTimeOffset.UtcNow;
        while(pending.TryDequeue(out var signal))
            if(SSpawnStopRules.ShouldStop(state.Watch,signal,context,now))return Confirm(state,signal.Source);
        if(Environment.TickCount64<nextLocalCheck)return null;
        nextLocalCheck=Environment.TickCount64+200;
        if(mobs().Any(m=>m.NameId==state.Watch.NameId && m.Hp>0))
        {
            var signal=new SSpawnSignal(state.Watch.RunId,context.Scope,state.Watch.NameId,now,"本地已加载 S 怪");
            if(SSpawnStopRules.ShouldStop(state.Watch,signal,context,now))return Confirm(state,signal.Source);
        }
        return null;
    }
    private string Confirm(WatchState state,string source)
    {
        var line=state.Watch.Scope.Instance==0?"无分线":$"{state.Watch.Scope.Instance}线";
        LastStop=$"{state.WorldName}／{state.MapName}／{line}：{state.DisplayName}已出现，自动停止农怪（{source}）";
        Disarm();return LastStop;
    }
    private void OnChat(IHandleableChatMessage message)
    {
        var state=Volatile.Read(ref active);
        if(state==null || !string.Equals(message.Sender.TextValue.Trim(),"Sonar",StringComparison.OrdinalIgnoreCase))return;
        try
        {
            var payloads=message.Message.Payloads;
            var first=payloads.OfType<MapLinkPayload>().FirstOrDefault();
            if(first==null)return;
            var index=payloads.IndexOf(first);
            var before=string.Concat(payloads.Take(index).OfType<TextPayload>().Select(p=>p.Text));
            var after=new StringBuilder();var links=1;
            foreach(var payload in payloads.Skip(index+1))
            {
                if(payload is MapLinkPayload)links++;
                if(payload is not TextPayload text)continue;
                var value=text.Text ?? "";
                var end=value.IndexOfAny(['\r','\n']);
                if(end>=0){after.Append(value.AsSpan(0,end));break;}
                after.Append(value);
            }
            var map=Svc.Data.GetExcelSheet<Map>().GetRowOrDefault(first.Map.RowId);
            var input=new SSpawnChat(message.Sender.TextValue,before,after.ToString(),links,first.TerritoryType.RowId,first.Map.RowId,map?.TerritoryType.RowId ?? 0);
            uint? Resolve(string name)=>worlds.TryGetValue(name,out var ids) && ids.Length==1?ids[0]:null;
            if(SSpawnStopRules.TryParseSonar(state.Watch,input,state.Names,Resolve,DateTimeOffset.UtcNow,out var signal) && signal.Scope==state.Watch.Scope)
            {
                pending.Enqueue(signal);
                while(pending.Count>64)pending.TryDequeue(out _);
            }
        }
        catch(Exception ex){Svc.Log.Warning(ex,"LuckyStarMOGAI ignored an unconfirmed S announcement");}
    }
    public void Dispose(){Svc.Chat.ChatMessage-=OnChat;Disarm();}
}
