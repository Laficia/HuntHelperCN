using LuckyStar;
using System.Numerics;

var count=0;
void Check(string label,Action test){test();count++;Console.WriteLine("PASS "+label);}
void Assert(bool value,string message="assertion failed"){if(!value)throw new Exception(message);}
var profile=FarmProfiles.Profile(0,[true,true]);
(Fake, FarmSession) Farm(){var h=new Fake();h.Ctx=h.Ctx with{Position=Vector3.Zero};h.Seen=[new(1,5750,Vector3.Zero,100,100),new(2,5751,new(1,0,0),100,100)];return(h,new(h){MountBetweenTargets=false});}
(Fake,RubyPatrol) Ruby(bool[]? caves=null){var h=new Fake();var r=new RubyPatrol(h){MountBetweenTargets=false};Assert(r.Start(profile,caves??[true,true,true],0));return(h,r);}
Check("new configuration enables one-click rotation management",()=>{
var config=new Configuration();var store=new ConfigurationStore();config.Initialize(store);
Assert(config.AllowRotationStart && config.Version==2 && store.SavedAllowStart && store.SavedVersion==2);
});
Check("v1 manual default is migrated once and the user can opt out afterward",()=>{
var config=new Configuration{Version=1,Rotation=RotationProvider.RotationSolverReborn,AllowRotationStart=false};var store=new ConfigurationStore();config.Initialize(store);
Assert(config.AllowRotationStart && store.Saves==1 && config.Rotation==RotationProvider.RotationSolverReborn);
config.AllowRotationStart=false;config.Save();config.Initialize(store);
Assert(!config.AllowRotationStart && !store.SavedAllowStart && store.Saves==2);
});
Check("0.0.0.1 profiles preserve all six original projects",()=>{Assert(FarmProfiles.Titles.Length==7);for(var i=1;i<7;i++)Assert(FarmProfiles.Preset(i,Enumerable.Repeat(true,FarmProfiles.NameIds[i].Length).ToArray()).Count>0);});
Check("CN name IDs match actual game sheet",()=>{Assert(FarmProfiles.NameIds[0].SequenceEqual([5750u,5751u]));Assert(FarmProfiles.NameIds[1].SequenceEqual([10701u,10697u,10698u]));Assert(FarmProfiles.NameIds[2].SequenceEqual([10463u,10461u,10462u]));Assert(FarmProfiles.NameIds[3].SequenceEqual([8598u,8599u,8789u]));});
Check("selected target types stay constrained",()=>Assert(FarmProfiles.Profile(0,[false,true]).Names.SequenceEqual([5751u])));
Check("3 caves match requested map coordinates",()=>Assert(RubyCaves.All.Select(c=>c.Map).SequenceEqual([new(15,14.5f),new(21,26),new(25,25.5f)])));
Check("portal IDs and positions retain native game pairs",()=>{Assert(RubyCaves.All.Select(c=>c.EnterId).SequenceEqual([6903392u,6903380u,6890619u]));foreach(var c in RubyCaves.All)Assert(Vector3.Distance(c.Interior,c.Exterior)<45);});
Check("empty route rejected without starting AE",()=>{var(h,s)=Farm();Assert(!s.Start(profile,[],0));Assert(h.Actions.Count==0);});
Check("empty selected monster types rejected",()=>{var(h,s)=Farm();Assert(!s.Start(profile with{Names=[]},[Vector3.Zero],0));});
Check("wrong territory rejected",()=>{var(h,s)=Farm();h.Ctx=h.Ctx with{Scope=new(1,147,0)};Assert(!s.Start(profile,[Vector3.Zero],0));});
Check("another navigation task never replaced",()=>{var(h,s)=Farm();h.NavBusy=true;Assert(!s.Start(profile,[Vector3.Zero],0));Assert(h.Actions.Count==0);});
Check("nav movement pause respected",()=>{var(h,s)=Farm();h.Allow=false;Assert(!s.Start(profile,[Vector3.Zero],0));});
Check("NaN route rejected",()=>{var(h,s)=Farm();Assert(!s.Start(profile,[new(float.NaN,0,0)],0));});
Check("ground farm never mounts or flies",()=>{var(h,s)=Farm();Assert(s.Start(profile,[new Vector3(50,0,0)],0));s.Tick(2000);Assert(h.Moves.Single().Fly==false);Assert(!h.Actions.Contains("mount"));});
Check("movement cancellation stops without resubmitting",()=>{var(h,s)=Farm();Assert(s.Start(profile,[new Vector3(50,0,0)],0));s.Tick(0);h.NavBusy=false;s.Tick(2000);Assert(!s.Running);Assert(h.Moves.Count==1);});
Check("ground nav has bounded stuck timeout",()=>{var(h,s)=Farm();Assert(s.Start(profile,[new Vector3(50,0,0)],0));s.Tick(0);s.Tick(21000);Assert(!s.Running);});
Check("actual live-to-dead counts exactly once",()=>{var(h,s)=Farm();Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);h.Seen=[h.Seen[0] with{Hp=0},h.Seen[1]];s.Tick(1000);s.Tick(2000);Assert(s.Deaths.GetValueOrDefault(5750u)==1);});
Check("disappearance is not counted as death",()=>{var(h,s)=Farm();Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);h.Seen=[];s.Tick(1000);Assert(s.Deaths.Count==0);});
Check("preexisting corpses do not count",()=>{var(h,s)=Farm();h.Seen=[h.Seen[0] with{Hp=0},h.Seen[1]];Assert(s.Start(profile with{RequireAllTargets=false},[Vector3.Zero],0));s.Tick(0);Assert(s.Deaths.Count==0);});
Check("respawned same object ID can count another kill",()=>{var(h,s)=Farm();Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);h.Seen=[h.Seen[0] with{Hp=0},h.Seen[1]];s.Tick(1000);h.Seen=[h.Seen[0] with{Hp=100},h.Seen[1]];s.Tick(2000);h.Seen=[h.Seen[0] with{Hp=0},h.Seen[1]];s.Tick(3000);Assert(s.Deaths[5750]==2);});
Check("seeing S stops farm and never targets S",()=>{var(h,s)=Farm();Assert(s.Start(profile,[Vector3.Zero],0));h.Seen.Add(new(99,5984,Vector3.Zero,1000,1000));s.Tick(0);Assert(!s.Running);Assert(!h.Actions.Contains("focus:99"));});
Check("world change stops current farm",()=>{var(h,s)=Farm();Assert(s.Start(profile,[Vector3.Zero],0));h.Ctx=h.Ctx with{Scope=new(2,613,0)};s.Tick(1000);Assert(!s.Running);});
Check("combat cleanup on explicit stop",()=>{var(h,s)=Farm();Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);Assert(h.Actions.Contains("combat:on"));s.Stop();Assert(h.Actions.Last()=="combat:off");});
Check("aggro while moving enables AE and stops owned movement",()=>{var(h,s)=Farm();Assert(s.Start(profile,[new Vector3(50,0,0)],0));s.Tick(0);h.Ctx=h.Ctx with{Combat=true};s.Tick(1000);Assert(s.Running);Assert(h.Actions.Contains("stop") && h.Actions.Last()=="combat:on");});
Check("unknown aggro stops instead of waiting with AE off",()=>{var(h,s)=Farm();Assert(s.Start(profile,[new Vector3(50,0,0)],0));s.Tick(0);h.Ctx=h.Ctx with{Combat=true};h.Seen=[];s.Tick(1000);Assert(!s.Running);});
Check("manual-target rotation approaches moving mob without returning to stale waypoint",()=>{var(h,s)=Farm();h.Engage=2.5f;h.Seen=[new(1,5750,new(20,0,0),100,100),new(2,5751,new(20,0,0),100,100)];Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);Assert(h.Moves.Single().Point==new Vector3(20,0,0));h.Ctx=h.Ctx with{Position=new(20,0,0)};h.NavBusy=false;s.Tick(1000);Assert(h.Moves.Count==1);Assert(h.Actions.Last()=="combat:on");});
Check("non-AE combat chase uses ground navigation and waits without repeated requests",()=>{var(h,s)=Farm();h.Engage=2.5f;h.Seen=[new(1,5750,new(12,0,0),100,100),new(2,5751,new(12,0,0),100,100)];Assert(s.Start(profile,[Vector3.Zero],0));h.Ctx=h.Ctx with{Combat=true};s.Tick(0);s.Tick(1000);Assert(h.Moves.Count==1 && !h.Moves[0].Fly);h.Ctx=h.Ctx with{Position=new(12,0,0)};s.Tick(2000);Assert(!h.NavBusy);});
Check("non-AE combat chase honors external vnav cancellation",()=>{var(h,s)=Farm();h.Engage=2.5f;h.Seen=[new(1,5750,new(12,0,0),100,100),new(2,5751,new(12,0,0),100,100)];Assert(s.Start(profile,[Vector3.Zero],0));h.Ctx=h.Ctx with{Combat=true};s.Tick(0);h.NavBusy=false;s.Tick(2000);Assert(!s.Running && h.Moves.Count==1);});
Check("manual-target combat switches from corpse to next nearby living mob",()=>{var(h,s)=Farm();h.Engage=2.5f;Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);h.Ctx=h.Ctx with{Combat=true};h.Seen=[h.Seen[0] with{Hp=0},h.Seen[1]];s.Tick(2000);Assert(s.Running && h.Actions.Last()=="focus:2");});
Check("mount retries are throttled",()=>{var(h,s)=Farm();s.MountBetweenTargets=true;Assert(s.Start(profile with{Ground=false},[new Vector3(50,0,0)],0));for(var i=0;i<10;i++)s.Tick(i*100);Assert(h.Actions.Count(a=>a=="mount")==1);});
Check("all three caves selected by default",()=>{var(h,r)=Ruby();Assert(r.Phase==RubyPhase.Scan);Assert(r.CaveIndex==0);});
Check("aggro during cave scan is fought and cleans up before resuming",()=>{var(h,r)=Ruby();h.Ctx=h.Ctx with{Position=RubyCaves.All[0].Interior};r.Tick(0);Assert(h.NavBusy);h.Ctx=h.Ctx with{Combat=true};h.Seen=[new(7,5750,h.Ctx.Position,100,100)];r.Tick(1000);Assert(r.Running && h.Actions.Last()=="combat:on" && !h.NavBusy);h.Ctx=h.Ctx with{Combat=false};r.Tick(2000);Assert(h.Actions.Contains("combat:off"));r.Tick(4000);Assert(h.Moves.Count==2);});
Check("cave defense switches target across consecutive melee kills",()=>{var(h,r)=Ruby();h.Engage=2.5f;h.Ctx=h.Ctx with{Position=RubyCaves.All[0].Interior,Combat=true};h.Seen=[new(7,5750,h.Ctx.Position,100,100),new(8,5751,h.Ctx.Position,100,100)];r.Tick(1000);h.Seen=[h.Seen[0] with{Hp=0},h.Seen[1]];r.Tick(2000);Assert(r.Running && h.Actions.Last()=="focus:8");});
Check("no selected cave rejected",()=>{var h=new Fake();Assert(!new RubyPatrol(h).Start(profile,[false,false,false],0));});
Check("surface location rejected",()=>{var h=new Fake();h.Ctx=h.Ctx with{Position=Vector3.Zero};Assert(!new RubyPatrol(h).Start(profile,[true,true,true],0));});
Check("underwater starts with approach, not mounting or taking off",()=>{var h=new Fake();h.Ctx=h.Ctx with{Diving=true,Position=RubyCaves.All[1].Exterior};var r=new RubyPatrol(h);Assert(r.Start(profile,[true,true,true],0));Assert(r.CaveIndex==1);Assert(r.Phase==RubyPhase.Approach);});
Check("loading animation produces no navigation or combat actions",()=>{var(h,r)=Ruby();var before=h.Actions.Count;h.Ctx=h.Ctx with{Ready=false,Loading=true,Scope=new(0,0,0)};r.Tick(1000);r.Tick(60000);Assert(r.Running);Assert(h.Actions.Count==before);});
Check("load timeout does not loop forever",()=>{var(h,r)=Ruby();h.Ctx=h.Ctx with{Ready=false,Loading=true};r.Tick(1000);r.Tick(122000);Assert(!r.Running);});
Check("world switch during loading stops",()=>{var(h,r)=Ruby();h.Ctx=h.Ctx with{Ready=false,Loading=true,Scope=new(2,613,0)};r.Tick(1000);Assert(!r.Running);});
Check("instance change after loading stops",()=>{var(h,r)=Ruby();h.Ctx=h.Ctx with{Scope=new(1,613,2)};r.Tick(1000);Assert(!r.Running);});
Check("logout stops whole patrol",()=>{var(h,r)=Ruby();h.Ctx=h.Ctx with{LoggedIn=false,Ready=false};r.Tick(1000);Assert(!r.Running);});
Check("transient login/player absence during cave fade is tolerated",()=>{var(h,r)=Ruby();h.Ctx=h.Ctx with{LoggedIn=false,Ready=false,Loading=true,Scope=new(0,0,0)};r.Tick(1000);Assert(r.Running);Assert(h.Actions.Count==0);});
Check("starting in third cave keeps current selected cave",()=>{var h=new Fake();h.Ctx=h.Ctx with{Position=RubyCaves.All[2].ScanPoint};var r=new RubyPatrol(h);Assert(r.Start(profile,[true,true,true],0));Assert(r.CaveIndex==2 && r.Phase==RubyPhase.Scan);});
Check("unselected current cave is exited before selected caves",()=>{var h=new Fake();var r=new RubyPatrol(h);Assert(r.Start(profile,[false,true,true],0));Assert(r.CaveIndex==0 && r.Phase==RubyPhase.ExitApproach);});
Check("death stops whole patrol",()=>{var(h,r)=Ruby();h.Ctx=h.Ctx with{Dead=true};r.Tick(1000);Assert(!r.Running);});
Check("mesh rebuild waits without repeated route requests",()=>{var(h,r)=Ruby();h.NavReady=false;r.Tick(1000);r.Tick(90000);Assert(r.Running);Assert(h.Moves.Count==0);});
Check("mesh readiness wait has timeout",()=>{var(h,r)=Ruby();h.NavReady=false;r.Tick(181000);Assert(!r.Running);});
foreach(var provider in Enum.GetValues<RotationProvider>())
Check($"one-click {provider} full three-cave loop including all six loading animations",()=>{
var(h,r)=Ruby();long now=3000;
var api=new RotationChecks.FakeRotationApi();using var rotation=new CombatRotationSession(api);
var config=new Configuration();config.Initialize(new ConfigurationStore());
rotation.Begin(provider,config.AllowRotationStart);h.CombatAction=rotation.SetCombat;
void Tick(){if(h.Ctx.Ready && !h.Ctx.Loading)Assert(rotation.CheckWhileReady()==null);r.Tick(now);now+=2500;Assert(r.Running,r.State);}
void Arrive(){h.Ctx=h.Ctx with{Position=h.Moves.Last().Point};h.NavBusy=false;Tick();}
void Fade(Vector3 destination,bool diving){var scope=h.Ctx.Scope;h.Ctx=h.Ctx with{Ready=false,Loading=true,Scope=new(0,0,0)};var actions=h.Actions.Count;Tick();now+=30000;Tick();Assert(actions==h.Actions.Count);h.Ctx=h.Ctx with{Ready=true,Loading=false,Scope=scope,Position=destination,Diving=diving};h.NavBusy=false;Tick();Tick();}
for(var caveIndex=0;caveIndex<3;caveIndex++){
Assert(r.CaveIndex==caveIndex);var cave=RubyCaves.All[caveIndex];
if(Vector3.Distance(h.Ctx.Position,cave.ScanPoint)>4){Tick();Arrive();}else Tick();
Assert(r.Phase==RubyPhase.Farm);
h.Seen=[new((ulong)(caveIndex*10+1),5750,cave.ScanPoint,100,100),new((ulong)(caveIndex*10+2),5751,cave.ScanPoint,100,100)];Tick();
Assert(h.Actions.Last()=="combat:on");h.Seen=h.Seen.Select(m=>m with{Hp=0}).ToList();Tick();Assert(r.Phase==RubyPhase.ExitApproach);
Tick();Arrive();Assert(r.Phase==RubyPhase.Exit);Tick();Assert(h.Moves.Last().Direct && !h.Moves.Last().Fly);
Fade(cave.Exterior,true);Assert(r.Phase==RubyPhase.Approach);var next=RubyCaves.All[(caveIndex+1)%3];
Tick();Assert(!h.Moves.Last().Direct && h.Moves.Last().Fly);Arrive();Assert(r.Phase==RubyPhase.Enter);
Tick();Assert(h.Moves.Last().Direct && h.Moves.Last().Fly);Fade(next.Interior,false);Assert(r.Phase==RubyPhase.Scan);
}
Assert(r.CaveIndex==0);Assert(r.CompletedVisits==3);Assert(r.Deaths[5750]==3 && r.Deaths[5751]==3);
r.Stop();rotation.End();
if(provider==RotationProvider.RotationSolverReborn)
Assert(api.Calls.Where(c=>c.Call==RotationCall.RsrMode).Select(c=>(string)c.Arguments[0]).SequenceEqual(Enumerable.Range(0,3).SelectMany(_=>new[]{"Henched","Off"})));
if(provider==RotationProvider.Prome)Assert(api.Count(RotationCall.PromeStart)==3 && api.Count(RotationCall.PromeStop)==3);
if(provider==RotationProvider.WrathCombo)Assert(api.Count(RotationCall.WrathRegister)==1 && api.Count(RotationCall.WrathRelease)==1);
});
count+=RotationChecks.Run();
count+=FlowChecks.Run();
Console.WriteLine($"ALL {count} CHECKS PASSED");

sealed class Fake:IFarmHost
{
 public FarmContext Ctx=new(true,new(1,613,0),RubyCaves.All[0].ScanPoint,false,false,false,false,false,false);
 public FarmContext Context=>Ctx;
 public List<FarmMob> Seen=[];public IReadOnlyList<FarmMob> Mobs=>Seen;
 public bool NavReady=true,NavBusy,Allow=true;
 public float Engage=25;public float EngageDistance=>Engage;
 public bool NavigationReady=>NavReady;public bool NavigationBusy=>NavBusy;public bool MovementAllowed=>Allow;
 public List<string> Actions=[];public List<(Vector3 Point,bool Fly,bool Direct)> Moves=[];
 public List<FarmContext> MoveContexts=[];
 public bool Move(Vector3 p,bool fly){Actions.Add("move");Moves.Add((p,fly,false));MoveContexts.Add(Ctx);NavBusy=true;return true;}
 public bool MoveDirect(Vector3 p,bool fly){Actions.Add("direct");Moves.Add((p,fly,true));MoveContexts.Add(Ctx);NavBusy=true;return true;}
 public void StopMovement(){Actions.Add("stop");NavBusy=false;}
 public Action<bool>? CombatAction;
 public Func<Vector3,Vector3?>? GroundQuery;
 public Vector3? GroundPoint(Vector3 position)=>GroundQuery is { } query?query(position):position;
 public void SetCombat(bool e){CombatAction?.Invoke(e);Actions.Add(e?"combat:on":"combat:off");}
 public void Focus(ulong id)=>Actions.Add("focus:"+id);
 public void ClearTarget()=>Actions.Add("clear");
 public void Mount()=>Actions.Add("mount");public void TakeOff()=>Actions.Add("takeoff");public void Dismount()=>Actions.Add("dismount");
}

