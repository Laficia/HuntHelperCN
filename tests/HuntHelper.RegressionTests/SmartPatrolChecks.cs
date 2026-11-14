using HuntHelper;
using HuntHelper.Managers.Automation;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json;
using System.Numerics;

internal static class SmartPatrolChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("a loaded monster beside another spawn point cannot falsely cover a distant target", AdjacentSpawn),
        ("Shaaloani adds twenty metres only to flying patrol targets without mutating stored coordinates", Shaaloani),
        ("preset rank filtering preserves only selected A or B point order", PresetRanks),
        ("invalid presets are rejected without moving or changing worlds", InvalidPreset),
        ("travel planner picks the closest unlocked candidate rather than the first crystal", CrystalChoice),
        ("same-map shortcuts account for teleport overhead savings threshold and disabled preference", TravelCost),
        ("cross-map travel still chooses a target-adjacent crystal with shortcuts disabled", CrossMap),
        ("unknown crystal geometry is only a cross-map fallback", MissingGeometry),
        ("scan memory rejects foreign stale dead Sonar imported and unknown-time records", MemoryGuards),
        ("complete manual scan memory skips a map without rescanning mounting moving or teleporting", CompleteMemory),
        ("partial memory counts a single species without inventing coverage of its old location", PartialMemory),
        ("disabled memory ignores old discoveries and equal IDs cannot complete a two-species map", MemoryPreference),
        ("live detection at a target avoids flying to that target and waits for supplemental loading", EarlyDetection),
        ("learned distant stopping falls back to the configured approach when no target is confirmed", AdaptiveFallback),
        ("disabling adaptive distance retains the configured radius", AdaptiveDisabled),
        ("three-dimensional target confirmation rejects unrelated floors", VerticalGuard),
        ("preset routes always retain their original point order", RouteChoice),
        ("same-map teleport waits for busy and actual arrival instead of treating the old map as arrival", SameMapArrival),
        ("same-map teleport stops on instance change before scanning the wrong line", TeleportLine),
        ("shortcut teleport cannot continue after stop combat world change or timeout", TeleportStops),
        ("teleport cooldown prevents repeated same-map shortcuts at the next point", Cooldown),
        ("finder optimization preferences default on and persist explicit disabled choices", Preferences),
    ];
    private static readonly DateTime Now=new(2026,11,11,0,0,0,DateTimeKind.Utc);
    private static PatrolContext Context => new(101,10,20,1,Vector3.Zero,true,true,false,false);
    private static PatrolRoute Route(bool optimize=false,params float[] x) => new(){TerritoryId=10,MapId=20,Expansion=1,
        Points=(x.Length==0?new float[]{600,900}:x).Select(v=>new PatrolPoint{Position=new(v,0,0)}).ToList()};
    private static PatrolOptions Options()=>new(){Mount=false,Takeoff=false,Radius=10,WaitMs=1000};
    private static void Check(bool ok){if(!ok)throw new Exception("Smart patrol assertion failed");}
    private static Vector2 Map(float x)=>new(x/50+21.48f,21.48f);
    private static PatrolCrystal Crystal(uint id,float x)=>new(id,"水晶"+id,Map(x));
    private static void CrystalChoice()
    {
        var p=PatrolTravelPlanner.Select(Context,Route(),Route().Points[0],Options(),new[]{Crystal(1,-500),Crystal(2,590)},100,6);
        Check(p?.Crystal.Id==2 && p.DirectSeconds-p.ViaSeconds>=10);
    }
    private static void AdjacentSpawn()
    {
        var o=Options();o.Radius=75;var(h,s)=Start(Route(false,200),o);h.Found=new[]{new PatrolDetection(1,true,new(125,0,0))};
        s.Tick(0);s.Tick(1);Check(h.Moves==1 && s.State==PatrolState.Moving && s.CompletedPoints==0);
    }
    private static void Shaaloani()
    {
        var point=new Vector3(100,50,200);Check(PatrolTravelPlanner.FlightTarget(1190,point,true)==new Vector3(100,70,200));
        Check(PatrolTravelPlanner.FlightTarget(1190,point,false)==point && PatrolTravelPlanner.FlightTarget(1189,point,true)==point);
        Check(point==new Vector3(100,50,200));
    }
    private static void PresetRanks()
    {
        var route=Route(false,100,200,300);route.Points[0].ForA=false;route.Points[1].ForB=false;
        var(h,s)=Start(route);Check(s.CurrentRoute!.Points.Select(p=>p.Position!.Value.X).SequenceEqual(new float[]{200,300}));
        var o=Options();o.RankA=false;o.RankB=true;(h,s)=Start(route,o);
        Check(s.CurrentRoute!.Points.Select(p=>p.Position!.Value.X).SequenceEqual(new float[]{100,300}));
    }
    private static void InvalidPreset()
    {
        var h=new Host();var s=new PatrolSession(h);var route=Route();route.Points[0].Position=new(float.NaN,0,0);
        Check(!s.Start(new[]{route},Options(),0) && h.Moves==0 && h.Teleports==0);
    }
    private static void TravelCost()
    {
        var o=Options();var route=Route();
        Check(PatrolTravelPlanner.Select(Context,route,route.Points[0],o,new[]{Crystal(1,590)},100,20)==null);
        o.TeleportSeconds=10;Check(PatrolTravelPlanner.Select(Context,route,route.Points[0],o,new[]{Crystal(1,590)},100,20)!=null);
        o.SmartTeleport=false;Check(PatrolTravelPlanner.Select(Context,route,route.Points[0],o,new[]{Crystal(1,590)},100,6)==null);
    }
    private static void CrossMap()
    {
        var o=Options();o.SmartTeleport=false;var route=Route();
        Check(PatrolTravelPlanner.Select(Context with{Territory=9},route,route.Points[0],o,new[]{Crystal(1,0),Crystal(2,590)},100,20)?.Crystal.Id==2);
    }
    private static void MissingGeometry()
    {
        var route=Route();PatrolCrystal[] crystals=[new(1,"未知",null)];
        Check(PatrolTravelPlanner.Select(Context,route,route.Points[0],Options(),crystals,100,6)==null);
        Check(PatrolTravelPlanner.Select(Context with{Territory=9},route,route.Points[0],Options(),crystals,100,6)?.Crystal.Id==1);
        Check(PatrolTravelPlanner.Select(Context with{Territory=9},route,route.Points[0],Options(),Array.Empty<PatrolCrystal>(),100,6)==null);
    }
    private static HuntTrainMob Mob(uint world=101,uint territory=10,uint map=20,uint line=1)=>new("怪",1,territory,map,line,"",new(20,20),Now,false,world,"服")
        {Source=HuntRecordSource.Scan,LastObservedUtc=Now-TimeSpan.FromMinutes(5)};
    private static void MemoryGuards()
    {
        Check(PatrolTravelPlanner.RecentLivingScan(Mob(),Context,Now));
        foreach(var m in new[]{Mob(world:102),Mob(territory:11),Mob(map:21),Mob(line:2),Mob(world:0)})Check(!PatrolTravelPlanner.RecentLivingScan(m,Context,Now));
        foreach(var source in new[]{HuntRecordSource.Unknown,HuntRecordSource.Sonar,HuntRecordSource.Import})
        {var m=Mob();m.Source=source;Check(!PatrolTravelPlanner.RecentLivingScan(m,Context,Now));}
        foreach(var time in new DateTime?[]{null,Now-TimeSpan.FromMinutes(61),Now+TimeSpan.FromMinutes(1)})
        {var m=Mob();m.LastObservedUtc=time;Check(!PatrolTravelPlanner.RecentLivingScan(m,Context,Now));}
        var dead=Mob();dead.Dead=true;Check(!PatrolTravelPlanner.RecentLivingScan(dead,Context,Now));
    }
    private static (Host,PatrolSession) Start(PatrolRoute? route=null,PatrolOptions? options=null,PatrolDetection[]? known=null)
    {
        var h=new Host{Known=known??Array.Empty<PatrolDetection>()};var s=new PatrolSession(h);
        Check(s.Start(new[]{route??Route()},options??Options(),0));return(h,s);
    }
    private static void CompleteMemory()
    {
        var(h,s)=Start(known:new[]{new PatrolDetection(1,true),new PatrolDetection(2,true)});s.Tick(0);
        Check(s.State==PatrolState.Completed && h.Scans==0 && h.Moves==0 && h.Mounts==0 && h.Teleports==0);
    }
    private static void PartialMemory()
    {
        var(h,s)=Start(known:new[]{new PatrolDetection(1,true,new(600,0,0))});s.Tick(0);s.Tick(1);
        Check(s.FoundA==1 && h.Moves==1 && s.State==PatrolState.Moving);
        h.Found=new[]{new PatrolDetection(2,true,new(700,0,0))};s.Tick(2);Check(s.State==PatrolState.Completed && h.Stops==1);
    }
    private static void MemoryPreference()
    {
        var o=Options();o.UseRecordedHunts=false;var(h,s)=Start(options:o,known:new[]{new PatrolDetection(1,true),new PatrolDetection(2,true)});
        s.Tick(0);s.Tick(1);Check(s.FoundA==0 && h.Moves==1);
        (h,s)=Start(known:new[]{new PatrolDetection(1,true),new PatrolDetection(1,true)});s.Tick(0);Check(s.FoundA==1 && s.Active);
    }
    private static void EarlyDetection()
    {
        var(h,s)=Start();h.Found=new[]{new PatrolDetection(1,true,new(600,0,0))};s.Tick(0);s.Tick(1);
        Check(s.State==PatrolState.Scanning && h.Moves==0 && s.ObservedARange==600 && s.ApproachRadius==150);
        s.Tick(1000);Check(s.CompletedPoints==0);s.Tick(1001);Check(s.PointIndex==1 && s.Active);
    }
    private static void AdaptiveFallback()
    {
        var(h,s)=Start(Route(false,400));h.Found=new[]{new PatrolDetection(1,true,new(200,0,0))};s.Tick(0);s.Tick(1);
        h.Found=Array.Empty<PatrolDetection>();h.Context=h.Context with{Position=new(280,0,0)};s.Tick(2);
        Check(s.State==PatrolState.Scanning && h.Stops==1 && s.ApproachRadius>10);
        s.Tick(1002);Check(s.State==PatrolState.PreparingMove && s.CompletedPoints==0 && s.ApproachRadius==10);
        s.Tick(1003);Check(h.Moves==2);h.Context=h.Context with{Position=new(390,0,0)};s.Tick(1004);s.Tick(2004);
        Check(s.State==PatrolState.Completed);
    }
    private static void AdaptiveDisabled()
    {
        var o=Options();o.AdaptiveDetection=false;var(h,s)=Start(Route(false,400),o);h.Found=new[]{new PatrolDetection(1,true,new(200,0,0))};
        s.Tick(0);s.Tick(1);h.Context=h.Context with{Position=new(280,0,0)};s.Tick(2);Check(s.State==PatrolState.Moving && s.ApproachRadius==10);
    }
    private static void VerticalGuard()
    {
        var(h,s)=Start();h.Found=new[]{new PatrolDetection(1,true,new(600,500,0))};s.Tick(0);s.Tick(1);
        Check(h.Moves==1 && s.State==PatrolState.Moving);
    }
    private static void RouteChoice()
    {
        var(h,s)=Start(Route(true,600,100,300));s.Tick(0);s.Tick(1);Check(s.PointIndex==0 && h.Target.X==600);
        h.Context=h.Context with{Position=new(600,0,0)};s.Tick(2);s.Tick(1002);Check(s.PointIndex==1);
        (h,s)=Start(Route(false,600,100,300));s.Tick(0);s.Tick(1);Check(s.PointIndex==0 && h.Target.X==600);
    }
    private static (Host,PatrolSession) TeleportStart()
    {
        var(h,s)=Start();h.Plan=new(Crystal(2,500),new(500,0),100,25);s.Tick(0);s.Tick(1);
        Check(s.State==PatrolState.Teleporting && h.Teleports==1 && h.Moves==0);return(h,s);
    }
    private static void SameMapArrival()
    {
        var(h,s)=TeleportStart();s.Tick(2);Check(s.State==PatrolState.Teleporting && h.Moves==0);
        h.Context=h.Context with{Ready=false};s.Tick(3);h.Context=h.Context with{Ready=true};s.Tick(4);
        Check(s.State==PatrolState.Teleporting);h.Context=h.Context with{Position=new(500,0,0)};s.Tick(5);s.Tick(6);
        Check(s.State==PatrolState.Moving && h.Moves==1 && h.Teleports==1);
    }
    private static void TeleportLine()
    {
        var(h,s)=TeleportStart();var scans=h.Scans;h.Context=h.Context with{Instance=2,Position=new(500,0,0)};s.Tick(2);
        Check(!s.Active && h.Moves==0 && h.Scans==scans);
    }
    private static void TeleportStops()
    {
        foreach(var change in new[]{Context with{World=102},Context with{InCombat=true},Context with{Dead=true}})
        {var(h,s)=TeleportStart();h.Context=change;s.Tick(2);Check(!s.Active && h.Moves==0);}
        var(host,session)=TeleportStart();session.Stop();host.Context=Context with{Position=new(500,0,0)};session.Tick(10);Check(host.Moves==0);
        (host,session)=TeleportStart();session.Tick(120001);Check(!session.Active && host.Moves==0);
    }
    private static void Cooldown()
    {
        var(h,s)=TeleportStart();h.Context=h.Context with{Ready=false};s.Tick(2);h.Context=h.Context with{Ready=true,Position=new(500,0,0)};
        s.Tick(3);s.Tick(4);h.Context=h.Context with{Position=new(600,0,0)};s.Tick(5);s.Tick(1005);s.Tick(1006);
        Check(h.Teleports==1 && h.Moves==2 && s.PointIndex==1);
    }
    private static void Preferences()
    {
        var c=JsonConvert.DeserializeObject<Configuration>("{}")!;Check(c.HuntFinderSmartTeleport && c.HuntFinderAdaptiveDetection && c.HuntFinderUseRecordedHunts && c.HuntFinderTeleportSeconds==25);
        c.HuntFinderSmartTeleport=c.HuntFinderAdaptiveDetection=c.HuntFinderUseRecordedHunts=false;c.HuntFinderTeleportSeconds=50;
        var reload=JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(c))!;Check(!reload.HuntFinderSmartTeleport && !reload.HuntFinderAdaptiveDetection && !reload.HuntFinderUseRecordedHunts && reload.HuntFinderTeleportSeconds==50);
    }
    private sealed class Host:IPatrolHost
    {
        public PatrolContext Context{get;set;}=SmartPatrolChecks.Context;
        public bool Navigating{get;set;}
        public string NavigationMessage=>"停止";
        public int Scans,Moves,Teleports,Mounts,Stops;
        public Vector3 Target;
        public PatrolTeleport? Plan;
        public PatrolDetection[] Found=Array.Empty<PatrolDetection>(),Known=Array.Empty<PatrolDetection>();
        public bool Teleport(PatrolRoute r,out string error){Teleports++;error="";return true;}
        public PatrolTeleport? PlanTeleport(PatrolRoute r,PatrolPoint p,PatrolOptions o)=>Plan;
        public bool PrepareMount(bool m,bool f,out string message){Mounts++;message="";return true;}
        public bool Move(PatrolRoute r,PatrolPoint p,out Vector3 destination,out string error){Moves++;Target=destination=p.Position!.Value;error="";Navigating=true;return true;}
        public void StopNavigation(){Stops++;Navigating=false;}
        public IReadOnlyList<PatrolDetection> Scan(bool a,bool b){Scans++;return Found;}
        public IReadOnlyList<PatrolDetection> KnownDetections(bool a,bool b)=>Known;
    }
}
