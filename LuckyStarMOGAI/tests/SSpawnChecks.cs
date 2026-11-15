using LuckyStar;
using System.Numerics;

internal static class SSpawnChecks
{
    public static int Run()
    {
        var count=0;
        void Check(string name,Action action){action();count++;Console.WriteLine("PASS S spawn stop: "+name);}
        static void Assert(bool value){if(!value)throw new Exception("S spawn guard assertion failed");}
        var now=DateTimeOffset.Parse("2026-11-15T10:00:00Z");
        var watch=new SSpawnWatch(7,new(1183,147,0),24,2961,now.AddSeconds(-1));
        var context=new FarmContext(true,watch.Scope,Vector3.Zero,false,false,false,false,false,false);
        var live=new SSpawnSignal(watch.RunId,watch.Scope,watch.NameId,now,"test");
        var chat=new SSpawnChat("Sonar","Rank S: 蚓螈巨虫 ","北萨纳兰 ( 24.3 , 23.2 ) <银泪湖>",1,147,24,147);
        Check("all seven projects identify their primary S rather than the shared SS rank",()=>{
            Assert(FarmProfiles.SRankNameIds.SequenceEqual(new uint[]{5984,10618,10620,8890,5987,4380,2961}));
            for(var i=0;i<7;i++)Assert(FarmProfiles.Profile(i,FarmProfiles.NameIds[i].Select(_=>true).ToArray()).StopOnName==FarmProfiles.SRankNameIds[i]);
            Assert(!FarmProfiles.SRankNameIds.Contains(10615u) && !FarmProfiles.SRankNameIds.Contains(8915u));
        });
        string[] names=["蚓螈巨虫","Minhocao"];
        uint? World(string name)=>name switch{"银泪湖"=>1183,"另一服务器"=>1184,"INVALID"=>0,_=>null};
        bool Parse(SSpawnChat input,out SSpawnSignal signal,SSpawnWatch? target=null)=>SSpawnStopRules.TryParseSonar(target??watch,input,names,World,now,out signal);
        Check("matching current world, map, instance and S monster stops",()=>Assert(SSpawnStopRules.ShouldStop(watch,live,context,now)));
        Check("other S or SS targets cannot be armed as supported farm targets",()=>{
            foreach(var name in new uint[]{10615,8915,5990})
            {var other=watch with{NameId=name};Assert(!SSpawnStopRules.ShouldStop(other,live with{NameId=name},context,now));}
        });
        Check("a whitelisted S paired with the wrong farm territory is rejected",()=>{
            var wrong=watch with{NameId=5984};Assert(!SSpawnStopRules.ShouldStop(wrong,live with{NameId=5984},context,now));
        });
        Check("the same map on another server never stops",()=>Assert(!SSpawnStopRules.ShouldStop(watch,live with{Scope=new(1184,147,0)},context,now)));
        Check("the same server on another map never stops",()=>Assert(!SSpawnStopRules.ShouldStop(watch,live with{Scope=new(1183,613,0)},context,now)));
        Check("another instance never stops",()=>Assert(!SSpawnStopRules.ShouldStop(watch,live with{Scope=new(1183,147,2)},context,now)));
        Check("wrong S identity including a reused localized name never stops",()=>Assert(!SSpawnStopRules.ShouldStop(watch,live with{NameId=5025},context,now)));
        Check("current-world travel does not turn a home-world announcement into a match",()=>Assert(!SSpawnStopRules.ShouldStop(watch,live with{Scope=new(1184,147,0)},context,now)));
        Check("changed current world suspends the old farm watch",()=>Assert(!SSpawnStopRules.ShouldStop(watch,live,context with{Scope=new(1184,147,0)},now)));
        Check("changed current map suspends the old farm watch",()=>Assert(!SSpawnStopRules.ShouldStop(watch,live,context with{Scope=new(1183,613,0)},now)));
        Check("loading or an absent player cannot confirm the report",()=>{
            Assert(!SSpawnStopRules.ShouldStop(watch,live,context with{Ready=false,Loading=true,Scope=new(0,0,0)},now));
            Assert(!SSpawnStopRules.ShouldStop(watch,live,context with{LoggedIn=false},now));
        });
        Check("a previous farm run cannot stop a new run in the same scope",()=>Assert(!SSpawnStopRules.ShouldStop(watch,live with{RunId=6},context,now)));
        Check("observations before this run or older than thirty seconds are ignored",()=>{
            Assert(!SSpawnStopRules.ShouldStop(watch,live with{ObservedAt=now.AddSeconds(-2)},context,now));
            Assert(!SSpawnStopRules.ShouldStop(watch with{StartedAt=now.AddMinutes(-2)},live with{ObservedAt=now.AddSeconds(-31)},context,now));
        });
        Check("future observations and invalid bindings cannot stop",()=>{
            Assert(!SSpawnStopRules.ShouldStop(watch,live with{ObservedAt=now.AddSeconds(2)},context,now));
            Assert(!SSpawnStopRules.ShouldStop(null,live,context,now));Assert(!SSpawnStopRules.ShouldStop(watch with{MapId=0},live,context,now));
        });
        Check("real CN Sonar announcement resolves the explicit server",()=>{
            Assert(Parse(chat,out var signal));Assert(signal.Scope==watch.Scope && SSpawnStopRules.ShouldStop(watch,signal,context,now));
        });
        Check("a valid foreign-world Sonar announcement still cannot stop the current farm",()=>{
            Assert(Parse(chat with{AfterLink="北萨纳兰 (24.3,23.2) <另一服务器>"},out var signal));Assert(!SSpawnStopRules.ShouldStop(watch,signal,context,now));
        });
        Check("missing or unknown world names are never inferred from the player",()=>{
            foreach(var after in new[]{"北萨纳兰 (24.3,23.2)","北萨纳兰 <未知服务器>","北萨纳兰 <INVALID>"})Assert(!Parse(chat with{AfterLink=after},out _));
        });
        Check("multiple world tags are rejected even when they repeat the same name",()=>{
            Assert(!Parse(chat with{AfterLink="<银泪湖> <另一服务器>"},out _));Assert(!Parse(chat with{AfterLink="<银泪湖> <银泪湖>"},out _));
        });
        Check("ambiguous world-name resolution cannot stop",()=>Assert(!SSpawnStopRules.TryParseSonar(watch,chat,names,_=>null,now,out _)));
        Check("player chat and forwarded text are not trusted Sonar reports",()=>Assert(!Parse(chat with{Sender="某玩家"},out _)));
        Check("A rank, SS rank, other monster and malformed headers are ignored",()=>{
            foreach(var before in new[]{"Rank A: 蚓螈巨虫","Rank SS: 蚓螈巨虫","Rank S: 巨大鳐","消息：Rank S: 蚓螈巨虫"})Assert(!Parse(chat with{BeforeLink=before},out _));
        });
        Check("death and history reports are ignored",()=>{
            foreach(var suffix in new[]{" was just killed"," DEAD"," 已被击杀"," 已死亡"," 已讨伐"," 历史记录"})Assert(!Parse(chat with{AfterLink=chat.AfterLink+suffix},out _));
        });
        Check("a plain coordinate string cannot substitute for a structured map link",()=>Assert(!Parse(chat with{PrimaryLineMapLinks=0},out _)));
        Check("multiple primary-line map links are ambiguous",()=>Assert(!Parse(chat with{PrimaryLineMapLinks=2},out _)));
        Check("a wrong territory or conflicting map-row territory is rejected",()=>{
            Assert(!Parse(chat with{TerritoryId=613},out _));Assert(!Parse(chat with{MapTerritoryId=613},out _));
        });
        Check("a different map ID in the same territory is not silently accepted",()=>Assert(!Parse(chat with{MapId=25},out _)));
        Check("cross-world icon glyphs and fullwidth world brackets normalize correctly",()=>{
            Assert(Parse(chat with{AfterLink="北萨纳兰 ＜\ue05d银泪湖＞"},out var signal));Assert(signal.Scope.World==1183);
        });
        Check("a matching explicit instance glyph stops only the same instance",()=>{
            var target=watch with{Scope=new(1183,147,2)};Assert(Parse(chat with{AfterLink="北萨纳兰 \ue0b2 <银泪湖>"},out var signal,target));
            Assert(SSpawnStopRules.ShouldStop(target,signal,context with{Scope=target.Scope},now));
            Assert(!SSpawnStopRules.ShouldStop(watch,signal,context,now));
        });
        Check("an omitted instance is not guessed for an instanced farm",()=>{
            var target=watch with{Scope=new(1183,147,2)};Assert(Parse(chat,out var signal,target));Assert(!SSpawnStopRules.ShouldStop(target,signal,context with{Scope=target.Scope},now));
        });
        Check("conflicting instance glyph and text are rejected",()=>Assert(!Parse(chat with{AfterLink="北萨纳兰 \ue0b2 i1 <银泪湖>"},out _)));
        Check("Lifestream additions on later lines cannot supply or override the world",()=>{
            Assert(Parse(chat with{AfterLink=chat.AfterLink+"\n青磷精炼所 [Lifestream] <另一服务器>"},out var signal));Assert(signal.Scope.World==1183);
            Assert(!Parse(chat with{AfterLink="北萨纳兰\n<Lifestream> <银泪湖>"},out _));
        });
        Check("English Sonar species names retain the same strict identity",()=>{
            Assert(Parse(chat with{BeforeLink="Rank S: Minhocao "},out var signal));Assert(signal.NameId==2961);
        });
        Check("the outer owner can check a stop before invoking the unchanged legacy frame",()=>{
            LegacyRuntime.Reset();using var legacy=new LegacyFarmController(false);legacy.Start([(10,0,0)]);var actions=LegacyRuntime.Commands.Count;
            ECommons.DalamudServices.Svc.Framework.Tick();Assert(LegacyRuntime.Commands.Count==actions);
            if(SSpawnStopRules.ShouldStop(watch,live,context,now))legacy.Stop();else legacy.Tick(ECommons.DalamudServices.Svc.Framework);
            legacy.Tick(ECommons.DalamudServices.Svc.Framework);Assert(!legacy.IsRunning && !LegacyRuntime.Commands.Any(c=>c.StartsWith("/vnav flyto")));
        });
        return count;
    }
}
