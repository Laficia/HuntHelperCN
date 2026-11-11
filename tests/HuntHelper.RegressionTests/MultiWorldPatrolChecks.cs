using HuntHelper;
using HuntHelper.Managers.Automation;
using HuntHelper.Managers.Hunts.Models;
using Newtonsoft.Json;
using System.Numerics;

internal static class MultiWorldPatrolChecks
{
    public static readonly (string Name,Action Run)[] Tests =
    [
        ("multi-world visits follow checkbox insertion order and run every map on each world", VisitOrder),
        ("already being on the first selected world requires no Lifestream request", CurrentFirst),
        ("travel scans nothing during busy logout loading intermediate worlds or premature arrival", TravelWait),
        ("travel failure missing IPC unsupported destination and busy plugin prevent starting", Preconditions),
        ("rejected transfer does not scan or abort somebody else's Lifestream job", Rejected),
        ("Stop cancels only a transfer owned by this patrol and cannot restart after arrival", StopTransfer),
        ("combat timeout plugin removal and wrong-world idle stop the multi-world queue", TravelFailure),
        ("an unexpected manual world change during scanning still stops", UnexpectedWorld),
        ("world selections are frozen at Start and duplicate server IDs run once", Snapshot),
        ("single-world mode remains independent of missing Lifestream", SingleWorld),
        ("multi-world preferences preserve selection order and default off", Preferences),
        ("a completed first-world record set never leaks into a subsequent world's counts", RecordIsolation),
    ];
    private static readonly HuntTrainWorld A=new(101,"A服","测试",0),B=new(102,"B服","测试",0),C=new(103,"C服","测试",0);
    private static PatrolRoute Route(uint territory=10)=>new(){TerritoryId=territory,MapId=territory+10,Expansion=1,
        Points=new(){new(){Position=new Vector3(100,0,0)}}};
    private static PatrolOptions Options(params HuntTrainWorld[] worlds)=>new(){Worlds=worlds.ToList(),Mount=false,Takeoff=false,Radius=10,WaitMs=1000};
    private static void Check(bool ok){if(!ok)throw new Exception("Multi-world patrol assertion failed");}
    private sealed class Host:IPatrolHost
    {
        public PatrolContext Context{get;set;}=new(101,10,20,1,Vector3.Zero,true,true,false,false);
        public bool Navigating{get;set;}
        public string NavigationMessage=>"停止";
        public string WorldTravelError{get;set;}="";
        public bool WorldTravelBusy{get;set;}
        public string Problem="";
        public bool Accept=true;
        public List<uint> Changes=new(),ScannedWorlds=new();
        public int Aborts,Moves,Teleports;
        public IReadOnlyList<PatrolDetection> Known=Array.Empty<PatrolDetection>();
        public string WorldVisitProblem(HuntTrainWorld w)=>Problem;
        public bool ChangeWorld(HuntTrainWorld w,out string error){Changes.Add(w.WorldID);error=Accept?"":"拒绝";WorldTravelBusy=Accept;return Accept;}
        public void AbortWorldTravel(){Aborts++;WorldTravelBusy=false;}
        public bool Teleport(PatrolRoute r,out string error){Teleports++;Context=Context with{Territory=r.TerritoryId,Map=r.MapId};error="";return true;}
        public bool PrepareMount(bool m,bool f,out string message){message="";return true;}
        public bool Move(PatrolRoute r,PatrolPoint p,out Vector3 destination,out string error){Moves++;destination=p.Position!.Value;Context=Context with{Position=destination};error="";Navigating=true;return true;}
        public void StopNavigation(){Navigating=false;}
        public IReadOnlyList<PatrolDetection> Scan(bool a,bool b){ScannedWorlds.Add(Context.World);return Array.Empty<PatrolDetection>();}
        public IReadOnlyList<PatrolDetection> KnownDetections(bool a,bool b)=>Known;
        public void Arrive(uint world){WorldTravelBusy=false;Context=Context with{World=world,Ready=true,LoggedIn=true};}
    }
    private static void VisitOrder()
    {
        var h=new Host();var s=new PatrolSession(h);Check(s.Start(new[]{Route(),Route(11)},Options(C,B),0));
        long now=0;var seenMaps=new HashSet<(uint,uint)>();
        for(var i=0;i<60&&s.Active;i++)
        {
            s.Tick(now);if(s.State==PatrolState.ChangingWorld){h.Arrive(s.TargetWorld!.WorldID);s.Tick(++now);}
            if(s.State is PatrolState.PreparingMove or PatrolState.Moving or PatrolState.Scanning)seenMaps.Add((h.Context.World,h.Context.Territory));
            now+=1100;
        }
        Check(s.State==PatrolState.Completed && h.Changes.SequenceEqual(new uint[]{103,102}));
        Check(seenMaps.SetEquals(new(uint,uint)[]{(103,10),(103,11),(102,10),(102,11)}) && h.ScannedWorlds.All(w=>w is 102 or 103));
    }
    private static void CurrentFirst()
    {var h=new Host();var s=new PatrolSession(h);Check(s.Start(new[]{Route()},Options(A),0));s.Tick(0);s.Tick(1);Check(h.Changes.Count==0 && s.State==PatrolState.PreparingMove);}
    private static (Host,PatrolSession) Transfer()
    {var h=new Host();var s=new PatrolSession(h);Check(s.Start(new[]{Route()},Options(B),0));s.Tick(0);Check(s.State==PatrolState.ChangingWorld);return(h,s);}
    private static void TravelWait()
    {
        var(h,s)=Transfer();h.Context=h.Context with{LoggedIn=false,Ready=false,World=0};s.Tick(1);
        h.Context=h.Context with{LoggedIn=true,Ready=true,World=103};s.Tick(2);
        h.Context=h.Context with{World=102};s.Tick(3);Check(h.ScannedWorlds.Count==0 && h.Moves==0);
        h.WorldTravelBusy=false;h.Context=h.Context with{Ready=false};s.Tick(4);Check(s.State==PatrolState.ChangingWorld);
        h.Context=h.Context with{Ready=true};s.Tick(5);Check(s.State==PatrolState.PreparingMap && h.ScannedWorlds.Count==0);
        s.Tick(6);Check(h.ScannedWorlds.SequenceEqual(new uint[]{102}));
    }
    private static void Preconditions()
    {
        foreach(var h in new[]{new Host{WorldTravelError="未安装"},new Host{Problem="不可访问"},new Host{WorldTravelBusy=true}})
        {var s=new PatrolSession(h);Check(!s.Start(new[]{Route()},Options(B),0) && h.Changes.Count==0 && h.Aborts==0);}
    }
    private static void Rejected()
    {var h=new Host{Accept=false};var s=new PatrolSession(h);Check(s.Start(new[]{Route()},Options(B),0));s.Tick(0);Check(!s.Active && h.Aborts==0 && h.ScannedWorlds.Count==0);}
    private static void StopTransfer()
    {var(h,s)=Transfer();s.Stop();h.Arrive(102);s.Tick(100);Check(h.Aborts==1 && h.Moves==0 && h.ScannedWorlds.Count==0);s.Stop();Check(h.Aborts==1);}
    private static void TravelFailure()
    {
        var(h,s)=Transfer();h.Context=h.Context with{InCombat=true};s.Tick(1);Check(!s.Active&&h.Aborts==1);
        (h,s)=Transfer();s.Tick(600000);Check(!s.Active&&h.Aborts==1);
        (h,s)=Transfer();h.WorldTravelError="插件已卸载";s.Tick(1);Check(!s.Active&&h.Aborts==1);
        (h,s)=Transfer();s.Tick(1);h.WorldTravelBusy=false;s.Tick(4000);Check(!s.Active && h.ScannedWorlds.Count==0);
    }
    private static void UnexpectedWorld()
    {var h=new Host();var s=new PatrolSession(h);Check(s.Start(new[]{Route()},Options(A),0));s.Tick(0);s.Tick(1);h.Context=h.Context with{World=103};s.Tick(2);Check(!s.Active && h.ScannedWorlds.All(w=>w==101));}
    private static void Snapshot()
    {
        var h=new Host();var s=new PatrolSession(h);var options=Options(B,B,C);Check(s.Start(new[]{Route()},options,0));options.Worlds.Clear();
        Check(s.WorldCount==2 && s.TargetWorld!.WorldID==102);s.Tick(0);Check(h.Changes.Single()==102);
    }
    private static void SingleWorld()
    {var h=new Host{WorldTravelError="未安装"};var s=new PatrolSession(h);Check(s.Start(new[]{Route()},Options(),0));s.Tick(0);s.Tick(1);Check(h.Moves==1&&h.Changes.Count==0);}
    private static void Preferences()
    {
        var c=JsonConvert.DeserializeObject<Configuration>("{}")!;Check(!c.HuntFinderMultiWorld && c.HuntFinderWorlds.Count==0);
        c.HuntFinderMultiWorld=true;c.HuntFinderWorlds=new(){103,101,102};var copy=JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(c))!;
        Check(copy.HuntFinderMultiWorld&&copy.HuntFinderWorlds.SequenceEqual(c.HuntFinderWorlds));
    }
    private static void RecordIsolation()
    {
        var h=new Host{Known=new[]{new PatrolDetection(1,true),new PatrolDetection(2,true)}};var s=new PatrolSession(h);
        Check(s.Start(new[]{Route()},Options(A,B),0));s.Tick(0);s.Tick(1);Check(s.TargetWorld!.WorldID==102);
        h.Known=Array.Empty<PatrolDetection>();s.Tick(2);h.Arrive(102);s.Tick(3);s.Tick(4);s.Tick(5);
        Check(s.FoundA==0&&h.Moves==1&&h.ScannedWorlds.All(w=>w==102));
    }
}
