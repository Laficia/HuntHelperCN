using HuntHelper.Managers.Automation;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System.Numerics;

internal static class AutomationChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("patrol records current-world observations and scans a full route", Complete),
        ("patrol uses normal crystal teleport once and waits for arrival", Teleport),
        ("failed crystal teleport never attempts coordinate movement", TeleportFailure),
        ("world change cancels only owned patrol navigation", WorldChange),
        ("instance or unexpected map change stops a patrol", LocationChange),
        ("combat and death stop owned movement", Combat),
        ("missing player and logout stop scanning", Logout),
        ("failed path stops without silently claiming an area was scanned", PathFailure),
        ("interrupted movement cannot silently advance a route", Interrupted),
        ("patrol stops on mount or teleport timeout", Timeout),
        ("enough discovered A ranks advance to the next map", EnoughHunts),
        ("patrol snapshots routes against edits and imports", Snapshot),
        ("idle Stop does not stop somebody else's navigation", Ownership),
        ("dead, busy and unknown-world characters cannot start", Preconditions),
        ("death observation stays world map and instance scoped", DeathScope),
        ("automatic death revives only when a living monster is observed", Revival),
        ("manual death is preserved when living monsters are observed", ManualDeath),
        ("manual revival suppresses the old corpse until a living sighting", ManualRevival),
        ("damaged nearby disappearance has a two-second grace", MissingGrace),
        ("unseen healthy distant or unloaded monsters cannot be inferred dead", MissingGuards),
        ("changing maps or instances clears disappearance evidence", MissingTravel),
        ("delete and re-record do not reuse stale death evidence", NewRecord),
        ("recorded routes preserve coordinates and point order on disk", RoutePersistence),
        ("invalid route import preserves the old route and original file", InvalidImport),
        ("corrupt route files cannot be overwritten", CorruptRoutes),
        ("route geometry sharing and nearest-neighbor order remain independent of server records", RouteSharing)
    ];
    private static PatrolRoute Route(uint zone = 10) => new() { TerritoryId = zone, MapId = zone + 10, Expansion = 1,
        Name = "测试图", Points = new() { new() { Position = new Vector3(100, 0, 100) }, new() { Position = new Vector3(200, 0, 200) } } };
    private static PatrolOptions Options() => new() { Mount = false, Takeoff = false, Radius = 10, WaitMs = 1000 };
    private static (Host Host, PatrolSession Session) Start(params PatrolRoute[] routes)
    {
        var host = new Host(); var session = new PatrolSession(host);
        Check(session.Start(routes.Length == 0 ? new[] { Route() } : routes, Options(), 0));
        return (host, session);
    }
    private static void Complete()
    {
        var (h, s) = Start(); s.Tick(0); s.Tick(1); Check(h.Moves == 1 && s.State == PatrolState.Moving);
        h.Position(new Vector3(100, 0, 100)); s.Tick(2); Check(s.State == PatrolState.Scanning);
        s.Tick(1001); Check(s.PointIndex == 0); s.Tick(1002); s.Tick(1003);
        Check(s.PointIndex == 1 && h.Moves == 2); h.Position(new Vector3(200, 0, 200)); s.Tick(1004); s.Tick(2004);
        Check(s.State == PatrolState.Completed && !s.Active && h.Scans > 0);
    }
    private static void Teleport()
    {
        var (h, s) = Start(Route(11)); s.Tick(0); s.Tick(1); Check(h.Teleports == 1 && h.Moves == 0);
        h.Context = h.Context with { World = 0, Territory = 0, Map = 0, Ready = false }; s.Tick(2); Check(h.Moves == 0 && s.Active);
        h.Context = C with { Territory = 11, Map = 21, Instance = 2 }; s.Tick(3); s.Tick(4); Check(h.Moves == 1);
    }
    private static void TeleportFailure()
    { var (h,s) = Start(Route(11)); h.TeleportResult = false; s.Tick(0); Check(!s.Active && h.Moves == 0 && h.Teleports == 1); }
    private static void WorldChange()
    { var (h,s) = Start(); s.Tick(0); s.Tick(1); h.Context = h.Context with { World = 102 }; s.Tick(2); Check(!s.Active && h.Stops == 1); }
    private static void LocationChange()
    {
        foreach(var c in new[] { new Host().Context with { Instance = 2 }, new Host().Context with { Territory = 11 } })
        { var (h,s) = Start(); s.Tick(0); s.Tick(1); h.Context = c; s.Tick(2); Check(!s.Active && h.Stops == 1); }
    }
    private static void Combat()
    {
        foreach (var dead in new[] { false,true })
        { var (h,s) = Start(); s.Tick(0); s.Tick(1); h.Context = h.Context with { InCombat = !dead, Dead = dead }; s.Tick(2); Check(!s.Active && h.Stops == 1); }
    }
    private static void Logout()
    { var (h,s) = Start(); s.Tick(0); s.Tick(1); h.Context = h.Context with { LoggedIn = false }; s.Tick(2); Check(!s.Active && h.Stops == 1); }
    private static void PathFailure()
    { var (h,s) = Start(); h.MoveResult = false; s.Tick(0); s.Tick(1); Check(!s.Active && s.PointIndex == 0 && h.Stops == 0); }
    private static void Interrupted()
    { var (h,s) = Start(); s.Tick(0); s.Tick(1); h.Navigating = false; s.Tick(2); Check(!s.Active && s.PointIndex == 0); }
    private static void Timeout()
    {
        var (h,s) = Start(Route(11)); s.Tick(0); s.Tick(120000); Check(!s.Active && h.Moves == 0);
        (h,s) = Start(); h.MountReady = false; s.Tick(0); s.Tick(1); s.Tick(120000); Check(!s.Active && h.Moves == 0);
    }
    private static void EnoughHunts()
    { var (h,s) = Start(Route(),Route(11)); h.Found = new[] { new PatrolDetection(1,true), new PatrolDetection(2,true) }; s.Tick(0); Check(s.CurrentRoute!.TerritoryId == 11 && h.Moves == 0); }
    private static void Snapshot()
    { var route = Route(); var (h,s) = Start(route); route.Points.Clear(); s.Tick(0); s.Tick(1); Check(h.Moves == 1 && s.CurrentRoute!.Points.Count == 2); }
    private static void Ownership()
    { var h = new Host { Navigating = true }; var s = new PatrolSession(h); s.Stop(); Check(h.Stops == 0); }
    private static void Preconditions()
    {
        var h = new Host { Navigating = true }; var s = new PatrolSession(h); Check(!s.Start(new[]{Route()},Options(),0));
        h.Navigating = false; h.Context = h.Context with { World=0 }; Check(!s.Start(new[]{Route()},Options(),0));
        h.Context = new Host().Context with { Dead=true }; Check(!s.Start(new[]{Route()},Options(),0));
        h.Context = new Host().Context; Check(!s.Start(new[]{Route()},new PatrolOptions { RankA=false,RankB=false },0));
    }
    private static HuntTrainMob Mob(uint world=101,uint instance=1) => new("怪物", 5,10,20,instance,"地图",new(20,20),DateTime.UtcNow,
        false,world,"服务器","",new Vector3(100,0,100));
    private static HuntObservation Seen(uint hp=100) => new(5,new(100,0,100),hp,100);
    private static PatrolContext C => new(101,10,20,1,new(100,0,100),true,true,false,false);
    private static void DeathScope()
    {
        var m = Mob(); var b = Mob(102); var line=Mob(instance:2); var monitor = new HuntDeathMonitor();
        Check(monitor.Update(C,new[]{m,b,line},new[]{Seen(0)},0)); Check(m.Dead && !b.Dead && !line.Dead);
    }
    private static void Revival()
    { var m=Mob(); var d=new HuntDeathMonitor(); d.Update(C,new[]{m},new[]{Seen(0)},0); Check(m.Dead); d.Update(C,new[]{m},new[]{Seen()},1); Check(!m.Dead); }
    private static void ManualDeath()
    { var m=Mob(); m.Dead=true; var d=new HuntDeathMonitor(); d.ManualChange(m); d.Update(C,new[]{m},new[]{Seen()},0); Check(m.Dead); }
    private static void ManualRevival()
    {
        var m=Mob(); var d=new HuntDeathMonitor(); d.Update(C,new[]{m},new[]{Seen(0)},0); m.Dead=false; d.ManualChange(m);
        d.Update(C,new[]{m},new[]{Seen(0)},1); Check(!m.Dead); d.Update(C with {Ready=false},new[]{m},Array.Empty<HuntObservation>(),2);
        d.Update(C,new[]{m},new[]{Seen(0)},3); Check(!m.Dead); d.Update(C,new[]{m},new[]{Seen()},4); d.Update(C,new[]{m},new[]{Seen(0)},5); Check(m.Dead);
    }
    private static void MissingGrace()
    {
        var m=Mob(); var d=new HuntDeathMonitor(); d.Update(C,new[]{m},new[]{Seen(50)},0);
        d.Update(C,new[]{m},Array.Empty<HuntObservation>(),500); d.Update(C,new[]{m},Array.Empty<HuntObservation>(),2499); Check(!m.Dead);
        d.Update(C,new[]{m},Array.Empty<HuntObservation>(),2500); Check(m.Dead);
    }
    private static void MissingGuards()
    {
        foreach(var kind in new[]{0,1,2,3})
        {
            var m=Mob(); var d=new HuntDeathMonitor(); if(kind!=0) d.Update(C,new[]{m},new[]{Seen(kind==1?100u:50u)},0);
            var c=kind==2?C with {Position=Vector3.Zero}:kind==3?C with {Ready=false}:C;
            d.Update(c,new[]{m},Array.Empty<HuntObservation>(),500); d.Update(c,new[]{m},Array.Empty<HuntObservation>(),5000); Check(!m.Dead);
        }
    }
    private static void MissingTravel()
    {
        var m=Mob();var d=new HuntDeathMonitor();d.Update(C,new[]{m},new[]{Seen(50)},0);d.Update(C,new[]{m},Array.Empty<HuntObservation>(),1);
        d.Update(C with{Instance=2},new[]{m},Array.Empty<HuntObservation>(),1000);d.Update(C,new[]{m},Array.Empty<HuntObservation>(),5000);Check(!m.Dead);
    }
    private static void NewRecord()
    { var m=Mob();var d=new HuntDeathMonitor();d.Update(C,new[]{m},new[]{Seen(50)},0);m=Mob();d.Update(C,new[]{m},Array.Empty<HuntObservation>(),1);d.Update(C,new[]{m},Array.Empty<HuntObservation>(),5000);Check(!m.Dead); }
    private static void RoutePersistence()
    {
        using var temp=new TemporaryDirectory();var path=Path.Combine(temp.Path,"ScanRoutes.json");var store=new PatrolRouteStore(path);store.Put(Route());Check(store.Save());
        var reloaded=new PatrolRouteStore(path);Check(reloaded.Routes.Single().Points[1].Position==new Vector3(200,0,200));Check(!Directory.GetFiles(temp.Path,"*.tmp").Any());
    }
    private static void InvalidImport()
    {
        using var temp=new TemporaryDirectory();var path=Path.Combine(temp.Path,"ScanRoutes.json");var store=new PatrolRouteStore(path);store.Put(Route());store.Save();var saved=File.ReadAllText(path);
        var bad=Route();bad.Points[0].Position=new Vector3(float.NaN,0,0);Check(!store.Import(ExportImport.Export(new[]{bad})));Check(File.ReadAllText(path)==saved);
        Check(!store.Import(ExportImport.Export(new[]{Mob()})));Check(File.ReadAllText(path)==saved);
    }
    private static void CorruptRoutes()
    { using var temp=new TemporaryDirectory();var path=Path.Combine(temp.Path,"ScanRoutes.json");File.WriteAllText(path,"bad json");var store=new PatrolRouteStore(path);store.Put(Route());Check(!store.Save());Check(File.ReadAllText(path)=="bad json"); }
    private static void RouteSharing()
    {
        using var temp=new TemporaryDirectory();var route=Route();route.Points.Add(new(){Position=new Vector3(110,0,110)});PatrolRouteStore.Sort(route);
        Check(route.Points[1].Position==new Vector3(110,0,110));var store=new PatrolRouteStore(Path.Combine(temp.Path,"routes.json"));Check(store.Import(ExportImport.Export(new[]{route})));
        Check(store.Routes.Single().Points.Count==3);
    }
    private static void Check(bool value) { if(!value) throw new InvalidOperationException("Automation assertion failed"); }
    private sealed class Host : IPatrolHost
    {
        public PatrolContext Context {get;set;}=C with{Position=Vector3.Zero};
        public bool Navigating {get;set;}
        public string NavigationMessage=>"导航中断";
        public int Moves,Teleports,Stops,Scans;
        public bool MoveResult=true,TeleportResult=true,MountReady=true;
        public IReadOnlyList<PatrolDetection> Found=Array.Empty<PatrolDetection>();
        public void Position(Vector3 p)=>Context=Context with{Position=p};
        public bool Teleport(PatrolRoute r,out string error){Teleports++;error="无法传送";return TeleportResult;}
        public bool PrepareMount(bool mount,bool takeoff,out string message){message="坐骑";return MountReady;}
        public bool Move(PatrolRoute r,PatrolPoint p,out Vector3 destination,out string error){Moves++;destination=p.Position!.Value;error="无法寻路";Navigating=MoveResult;return MoveResult;}
        public void StopNavigation(){Stops++;Navigating=false;}
        public IReadOnlyList<PatrolDetection> Scan(bool a,bool b){Scans++;return Found.Where(f=>f.IsA?a:b).ToList();}
    }
}
