using LuckyStar;
using System.Numerics;

internal static class FlowChecks
{
    public static int Run()
    {
        var count=0;
        void Check(string name,Action test){test();count++;Console.WriteLine("PASS mounted goals: "+name);}
        static void Assert(bool ok,string message="flow assertion failed"){if(!ok)throw new Exception(message);}
        var profile=FarmProfiles.Profile(0,[true,true]) with{RequireAllTargets=false};
        Check("defaults enable mounted flight and each project has the right per-kind quota",()=>{
            var config=new Configuration();Assert(config.MountBetweenTargets && config.FlyBetweenTargets);
            for(var i=0;i<7;i++)
            {
                var p=FarmProfiles.Profile(i,Enumerable.Repeat(true,FarmProfiles.NameIds[i].Length).ToArray());
                Assert(p.Names.All(n=>p.Goals![n]==(i==5?50:100)));
                if(i>0)Assert(FarmProfiles.Preset(i,Enumerable.Repeat(true,p.Names.Length).ToArray()).All(v=>p.PointNames!.ContainsKey(v)));
            }
        });
        Check("two successive distant kills mount, fly, stop near range, dismount and resume fighting",()=>{
            var h=new Fake{Engage=2.5f};h.Ctx=h.Ctx with{Position=Vector3.Zero};
            h.Seen=[new(1,5750,new(50,0,0),100,100),new(2,5751,new(100,0,0),100,100)];
            var s=new FarmSession(h);Assert(s.Start(profile,h.Seen.Select(m=>m.Position),0));
            long now=0;
            void Tick(){s.Tick(now);now+=2000;Assert(s.Running,s.State);}
            for(var id=1;id<=2;id++)
            {

                Tick();Assert(h.Actions.Last()=="mount" && !h.NavBusy);
                h.Ctx=h.Ctx with{Mounted=true};Tick();Assert(h.Actions.Last()=="takeoff" && !h.NavBusy);
                h.Ctx=h.Ctx with{Flying=true};Tick();Assert(h.Moves.Last().Fly);
                h.Ctx=h.Ctx with{Position=h.Moves.Last().Point};Tick();Assert(!h.NavBusy && h.Actions.Last()=="dismount");
                Assert(h.Actions.Last()=="dismount" && !h.Actions.Skip(h.Actions.LastIndexOf("mount")).Contains("combat:on"));
                h.Ctx=h.Ctx with{Mounted=false,Flying=false};Tick();Assert(h.Actions.Last()=="combat:on");
                h.Ctx=h.Ctx with{Combat=true};Tick();Assert(h.Actions.Last()==$"focus:{id}");
                h.Seen[id-1]=h.Seen[id-1] with{Hp=0};h.Ctx=h.Ctx with{Combat=false};
            }
            Assert(h.Actions.Count(a=>a=="mount")==2 && h.Actions.Count(a=>a=="dismount")==2);
        });
        Check("already in engage range does not mount unnecessarily",()=>{
            var h=new Fake{Engage=2.5f};h.Seen=[new(1,5750,h.Ctx.Position,100,100)];
            var s=new FarmSession(h);Assert(s.Start(profile,[h.Ctx.Position],0));s.Tick(0);
            Assert(h.Actions.SequenceEqual(["focus:1","combat:on"]));
        });
        Check("after a kill the next route starts on the first 50ms update without stale waypoint delay",()=>{
            var h=new Fake{Engage=2.5f};h.Ctx=h.Ctx with{Position=Vector3.Zero};
            h.Seen=[new(1,5750,Vector3.Zero,100,100),new(2,5751,new(100,0,0),100,100)];
            var s=new FarmSession(h);Assert(s.Start(profile,[Vector3.Zero,new(100,0,0)],0));s.Tick(0);
            h.Seen[0]=h.Seen[0] with{Hp=0};s.Tick(50);
            Assert(s.Index==0 && h.Actions.Last()=="mount" && s.Deaths[5750]==1);
        });
        Check("next target is the nearest loaded eligible mob across the cave route",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=new(40,0,0)};
            h.Seen=[new(1,5750,Vector3.Zero,100,100),new(2,5751,new(60,0,0),100,100)];
            var s=new FarmSession(h){MountBetweenTargets=false};Assert(s.Start(profile,[Vector3.Zero,new(80,0,0)],0));s.Tick(0);
            Assert(h.Actions.Contains("focus:2") && !h.Actions.Contains("focus:1"));
        });
        Check("nearest selection keeps a living target locked when another mob moves closer",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=Vector3.Zero};h.Seen=[new(1,5750,new(2,0,0),100,100),new(2,5751,new(5,0,0),100,100)];
            var s=new FarmSession(h){MountBetweenTargets=false};Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);
            h.Seen[1]=h.Seen[1] with{Position=Vector3.Zero};s.Tick(50);Assert(h.Actions.Last()=="focus:1");
        });
        Check("first-target delay does not delay each new target at the same waypoint",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=Vector3.Zero};h.Seen=[new(1,5750,Vector3.Zero,100,100),new(2,5751,Vector3.One,100,100)];
            var s=new FarmSession(h){MountBetweenTargets=false,DelaySeconds=2};Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);Assert(!h.Actions.Contains("combat:on"));
            s.Tick(2000);Assert(h.Actions.Contains("focus:1"));h.Seen[0]=h.Seen[0] with{Hp=0};s.Tick(2050);Assert(h.Actions.Contains("focus:2"));
        });
        Check("mount-to-takeoff transition does not wait for the previous action retry throttle",()=>{
            var h=new Fake();var travel=new FarmTravel(h);Assert(!travel.Prepare(0,true));h.Ctx=h.Ctx with{Mounted=true};
            Assert(!travel.Prepare(50,true));Assert(h.Actions.SequenceEqual(["mount","takeoff"]));
        });
        Check("checkpoint counts restore without recounting old corpses or repeating quota notifications",()=>{
            var progress=new FarmProgress();progress.Reset(profile with{Goals=new Dictionary<uint,int>{{5750,7},{5751,10}}});
            progress.RestoreDeaths(new Dictionary<uint,int>{{5750,7},{5751,5}});Assert(progress.Done(5750) && !progress.Done(5751));
            progress.Observe([new(1,5750,Vector3.Zero,0,100)]);Assert(progress.Deaths[5750]==7 && progress.TakeCompletedKinds().Length==0);
        });
        Check("checkpoint resume requires the same scope, project, selected types and recent time",()=>{
            var now=DateTimeOffset.UtcNow;var c=new FarmCheckpoint(new(1,613,0),0,[5750,5751],new(){{5750,7},{5751,5}},now);
            Assert(c.Matches(new(1,613,0),0,[5750,5751],now));
            Assert(!c.Matches(new(2,613,0),0,[5750,5751],now));Assert(!c.Matches(new(1,613,1),0,[5750,5751],now));
            Assert(!c.Matches(new(1,613,0),1,[5750,5751],now));Assert(!c.Matches(new(1,613,0),0,[5750],now));
            Assert(!c.Matches(new(1,613,0),0,[5750,5751],now.AddMinutes(11)));
        });
        Check("flight never dismounts high above the monster",()=>{
            var h=new Fake{Engage=2.5f};h.Ctx=h.Ctx with{Position=new(0,20,0),Mounted=true,Flying=true};h.Seen=[new(1,5750,Vector3.Zero,100,100)];
            var s=new FarmSession(h);Assert(s.Start(profile,[Vector3.Zero],0));s.Tick(0);s.Tick(1000);
            Assert(h.NavBusy && h.Moves.Last().Fly && !h.Actions.Contains("dismount"));
        });
        Check("hovering 2.3m above the landing goal never cancels and resubmits every 50ms",()=>{
            var h=new Fake{Engage=2.5f};h.Ctx=h.Ctx with{Position=Vector3.Zero,Mounted=true,Flying=true};h.Seen=[new(1,5750,new(20,0,0),100,100)];
            var s=new FarmSession(h);Assert(s.Start(profile,[new(20,0,0)],0));s.Tick(0);var goal=h.Moves.Single().Point;
            h.Ctx=h.Ctx with{Position=goal+new Vector3(0,2.3f,0)};
            for(var now=50;now<=5000;now+=50)s.Tick(now);
            Assert(s.Running && h.Moves.Count==1 && s.RoutesSubmitted==1 && h.NavBusy);
            Assert(!h.Actions.Contains("stop") && !h.Actions.Contains("dismount"));
        });
        Check("flight lands beside the target and dismounts without another navigation request",()=>{
            var h=new Fake{Engage=2.5f};h.Ctx=h.Ctx with{Position=Vector3.Zero,Mounted=true,Flying=true};h.Seen=[new(1,5750,new(20,0,0),100,100)];
            var s=new FarmSession(h);Assert(s.Start(profile,[new(20,0,0)],0));s.Tick(0);var goal=h.Moves.Single().Point;
            Assert(Vector3.Distance(goal,h.Seen[0].Position)>=1.5f && Vector3.Distance(goal,h.Seen[0].Position)<=2.5f);
            h.Ctx=h.Ctx with{Position=goal+new Vector3(0,0.1f,0)};s.Tick(50);Assert(h.Actions.Last()=="dismount");
            s.Tick(100);s.Tick(150);Assert(h.Moves.Count==1 && !h.NavBusy);
            h.Ctx=h.Ctx with{Position=goal,Mounted=false,Flying=false};s.Tick(200);Assert(h.Actions.Last()=="combat:on");
        });
        Check("a flight that ends above ground stops once instead of resetting navigation timeouts",()=>{
            var h=new Fake{Engage=2.5f};h.Ctx=h.Ctx with{Position=Vector3.Zero,Mounted=true,Flying=true};h.Seen=[new(1,5750,new(20,0,0),100,100)];
            var s=new FarmSession(h);Assert(s.Start(profile,[new(20,0,0)],0));s.Tick(0);
            h.Ctx=h.Ctx with{Position=h.Moves[0].Point+new Vector3(0,2.3f,0)};h.NavBusy=false;s.Tick(2000);s.Tick(4000);
            Assert(!s.Running && h.Moves.Count==1 && !h.Actions.Contains("dismount"));
        });
        Check("missing walkable ground refuses the approach without submitting a flight",()=>{
            var h=new Fake{Engage=2.5f,GroundQuery=_=>null};h.Ctx=h.Ctx with{Position=Vector3.Zero,Mounted=true,Flying=true};h.Seen=[new(1,5750,new(20,0,0),100,100)];
            var s=new FarmSession(h);Assert(s.Start(profile,[new(20,0,0)],0));s.Tick(0);
            Assert(!s.Running && s.State.Contains("落地点") && h.Moves.Count==0);
        });
        Check("mount failure while visiting an empty waypoint is bounded",()=>{
            var h=new Fake();h.Seen=[];var s=new FarmSession(h);Assert(s.Start(profile,[h.Ctx.Position+new Vector3(50,0,0)],0));
            s.Tick(0);s.Tick(14900);s.Tick(15100);Assert(!s.Running && s.State.Contains("上坐骑超时") && h.Moves.Count==0);
        });
        Check("mount cast is allowed to finish before another action",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Casting=true};var travel=new FarmTravel(h);
            Assert(!travel.Prepare(0,true));Assert(h.Actions.Count==0);
            h.Ctx=h.Ctx with{Casting=false};Assert(!travel.Prepare(2000,true));Assert(h.Actions.SequenceEqual(["mount"]));
        });
        Check("no mount request while combat is active",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Combat=true};var travel=new FarmTravel(h);Assert(!travel.Prepare(0,true));Assert(h.Actions.Count==0);
        });
        Check("underwater route waits for mounted flag and never asks to take off",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Diving=true,Position=RubyCaves.All[0].Exterior+new Vector3(10,0,0)};
            var r=new RubyPatrol(h);Assert(r.Start(profile,[true,true,true],0));r.Tick(0);
            Assert(h.Actions.SequenceEqual(["mount"]) && h.Moves.Count==0);
            h.Ctx=h.Ctx with{Mounted=true};r.Tick(2000);Assert(h.Moves.Single().Fly && !h.Actions.Contains("takeoff"));
        });
        Check("cave scan uses mounted flight",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=RubyCaves.All[0].Interior};var r=new RubyPatrol(h);
            Assert(r.Start(profile,[true,true,true],0));r.Tick(0);Assert(h.Actions.Last()=="mount");
            h.Ctx=h.Ctx with{Mounted=true};r.Tick(2000);Assert(h.Actions.Last()=="takeoff");
            h.Ctx=h.Ctx with{Flying=true};r.Tick(4000);Assert(h.Moves.Single().Fly);
        });
        Check("Tamamizu starts on land and selects nearest enabled cave",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=RubyCaves.Tamamizu.ScanPoint};var r=new RubyPatrol(h);
            Assert(r.Start(profile,[true,true,true],0));Assert(r.Phase==RubyPhase.TownExitApproach && r.CaveIndex==2);
            r.Tick(0);Assert(h.Actions.Last()=="mount");h.Ctx=h.Ctx with{Mounted=true};r.Tick(2000);
            Assert(h.Moves.Single().Point==RubyCaves.Tamamizu.Interior && !h.Moves[0].Fly && !h.Actions.Contains("takeoff"));
        });
        Check("Tamamizu exit, loading and mounted diving continue without another start",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=RubyCaves.Tamamizu.Interior,Mounted=true};var r=new RubyPatrol(h);
            Assert(r.Start(profile,[true,true,true],0));r.Tick(0);Assert(r.Phase==RubyPhase.TownExit);
            r.Tick(2000);r.Tick(4000);
            Assert(h.Moves.Last().Direct && !h.Moves.Last().Fly && h.MoveContexts.Last().Mounted && !h.Actions.Contains("dismount"));
            var actions=h.Actions.Count;h.Ctx=h.Ctx with{Ready=false,Loading=true};r.Tick(6000);Assert(h.Actions.Count==actions);
            h.Ctx=h.Ctx with{Ready=true,Loading=false,Position=RubyCaves.Tamamizu.Exterior,Diving=true};h.NavBusy=false;
            r.Tick(8000);r.Tick(11000);Assert(r.Phase==RubyPhase.Approach && r.Running);
            r.Tick(13000);r.Tick(15000);
            Assert(h.Moves.Last().Fly && h.MoveContexts.Last().Diving && h.MoveContexts.Last().Mounted);
        });
        Check("Tamamizu travel respects selected caves and rejects unrelated land",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=RubyCaves.Tamamizu.ScanPoint};var r=new RubyPatrol(h);
            Assert(r.Start(profile,[true,false,false],0) && r.CaveIndex==0);r.Stop();
            h.Ctx=h.Ctx with{Position=new(500,0,500)};Assert(!r.Start(profile,[true,true,true],1000));Assert(r.State.Contains("当前位置"));
        });
        Check("Tamamizu cancelled movement is not silently restarted",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=RubyCaves.Tamamizu.ScanPoint,Mounted=true};var r=new RubyPatrol(h);
            Assert(r.Start(profile,[true,true,true],0));r.Tick(0);h.NavBusy=false;r.Tick(2000);
            Assert(!r.Running && h.Moves.Count==1);
        });
        Check("last living cave target dying immediately starts the next visit without touring corpse waypoints",()=>{
            var h=new Fake();var at=h.Ctx.Position;
            h.Seen=[new(1,5750,at,100,100),new(2,5751,at+new Vector3(20,0,0),100,100)];
            var r=new RubyPatrol(h){MountBetweenTargets=false};Assert(r.Start(profile,[true,true,true],0));r.Tick(0);r.Tick(50);
            h.Seen=h.Seen.Select(m=>m with{Hp=0}).ToList();r.Tick(100);
            Assert(r.Running && r.Phase==RubyPhase.ExitApproach && r.CompletedVisits==1 && h.Moves.Count==0);
            r.Tick(150);Assert(h.Moves.Count==1 && h.Moves[0].Point==RubyCaves.All[0].Interior);
        });
        Check("cave exit keeps the mount and flying flag through the native portal",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=RubyCaves.All[0].Interior,Mounted=true,Flying=true};
            var r=new RubyPatrol(h);Assert(r.Start(profile,[false,true,true],0));r.Tick(0);Assert(r.Phase==RubyPhase.Exit);
            r.Tick(50);Assert(h.Moves.Single().Direct && h.Moves[0].Fly && h.MoveContexts[0].Mounted);
            Assert(!h.Actions.Contains("dismount"));
        });
        Check("an initially empty cave moves on after the short object grace instead of fifteen seconds",()=>{
            var h=new Fake();var r=new RubyPatrol(h){MountBetweenTargets=false};Assert(r.Start(profile,[true,true,true],0));r.Tick(0);
            r.Tick(50);Assert(r.CompletedVisits==0);r.Tick(2000);Assert(r.CompletedVisits==1 && r.Phase==RubyPhase.ExitApproach);
        });
        Check("one selected cave still waits locally instead of leaving for an unselected cave",()=>{
            var h=new Fake();h.Seen=[new(1,5750,h.Ctx.Position,100,100)];var r=new RubyPatrol(h){MountBetweenTargets=false};
            Assert(r.Start(profile,[true,false,false],0));r.Tick(0);r.Tick(50);h.Seen[0]=h.Seen[0] with{Hp=0};r.Tick(100);r.Tick(150);
            Assert(r.Running && r.Phase==RubyPhase.Farm && r.CompletedVisits==1 && h.Moves.Count==0);
        });
        Check("entry resumes after 350ms of ready scene without an extra two-second scan delay",()=>{
            var h=new Fake();var cave=RubyCaves.All[0];h.Ctx=h.Ctx with{Position=cave.Exterior,Diving=true,Mounted=true};
            var r=new RubyPatrol(h);Assert(r.Start(profile,[true,true,true],0));r.Tick(0);r.Tick(50);var moves=h.Moves.Count;
            h.Ctx=h.Ctx with{Ready=false,Loading=true};r.Tick(100);
            h.Ctx=h.Ctx with{Ready=true,Loading=false,Diving=false,Flying=true,Position=cave.Interior};h.NavBusy=false;
            r.Tick(200);r.Tick(500);Assert(h.Moves.Count==moves && r.Phase==RubyPhase.Enter);
            r.Tick(550);Assert(r.Phase==RubyPhase.Scan);r.Tick(600);Assert(h.Moves.Count==moves+1 && h.Moves.Last().Fly);
        });
        Check("scene stability timer restarts if the navigation mesh becomes unavailable",()=>{
            var h=new Fake();var cave=RubyCaves.All[0];h.Ctx=h.Ctx with{Position=cave.Exterior,Diving=true,Mounted=true};
            var r=new RubyPatrol(h);Assert(r.Start(profile,[true,true,true],0));r.Tick(0);r.Tick(50);
            h.Ctx=h.Ctx with{Ready=false,Loading=true};r.Tick(100);
            h.Ctx=h.Ctx with{Ready=true,Loading=false,Diving=false,Flying=true,Position=cave.Interior};h.NavBusy=false;r.Tick(200);
            h.NavReady=false;r.Tick(450);h.NavReady=true;r.Tick(600);r.Tick(900);Assert(r.Phase==RubyPhase.Enter);
            r.Tick(950);Assert(r.Phase==RubyPhase.Scan);
        });
        Check("entry does not resume until the real diving flag and destination have recovered",()=>{
            var h=new Fake();var cave=RubyCaves.All[0];h.Ctx=h.Ctx with{Position=cave.Exterior,Diving=true,Mounted=true};
            var r=new RubyPatrol(h);Assert(r.Start(profile,[true,true,true],0));r.Tick(0);r.Tick(50);var moves=h.Moves.Count;
            h.Ctx=h.Ctx with{Ready=false,Loading=true};r.Tick(100);
            h.Ctx=h.Ctx with{Ready=true,Loading=false,Position=cave.Interior};h.NavBusy=false;r.Tick(200);r.Tick(1000);
            Assert(r.Running && r.Phase==RubyPhase.Enter && h.Moves.Count==moves);
            h.Ctx=h.Ctx with{Diving=false,Flying=true};r.Tick(1100);r.Tick(1450);Assert(r.Phase==RubyPhase.Scan);
        });
        Check("completed kind stops being selected and all goals stop combat",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=Vector3.Zero};h.Seen=[new(1,5750,Vector3.Zero,100,100),new(2,5750,Vector3.One,100,100),new(3,5751,new(2,0,0),100,100)];
            var s=new FarmSession(h){MountBetweenTargets=false};var p=profile with{Goals=new Dictionary<uint,int>{{5750,1},{5751,1}}};
            Assert(s.Start(p,[Vector3.Zero],0));s.Tick(0);h.Seen[0]=h.Seen[0] with{Hp=0};s.Tick(2000);
            Assert(s.Running && s.Progress.Done(5750) && h.Actions.Last()=="combat:on" && h.Actions.Contains("focus:3") && !h.Actions.Contains("focus:2"));
            h.Seen[2]=h.Seen[2] with{Hp=0};s.Tick(4000);var before=h.Actions.Count;s.Tick(5000);
            Assert(!s.Running && s.Completed && h.Actions.Contains("clear") && before==h.Actions.Count);
        });
        Check("completed preset regions are skipped without mounting or flying there",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=Vector3.Zero};h.Seen=[new(1,5750,new(50,0,0),100,100)];
            var p=profile with{Goals=new Dictionary<uint,int>{{5750,1},{5751,1}},PointNames=new Dictionary<Vector3,uint>{{new(50,0,0),5750},{new(100,0,0),5751}}};
            var s=new FarmSession(h);Assert(s.Start(p,[new(50,0,0),new(100,0,0)],0));s.Progress.Observe(h.Seen);h.Seen[0]=h.Seen[0] with{Hp=0};s.Tick(0);
            Assert(s.Index==1 && h.Moves.Count==0 && !h.Actions.Contains("mount"));
        });
        Check("a completed kind alone in combat is not reengaged or mounted past",()=>{
            var h=new Fake();h.Ctx=h.Ctx with{Position=Vector3.Zero};h.Seen=[new(1,5750,Vector3.Zero,100,100)];
            var p=profile with{Goals=new Dictionary<uint,int>{{5750,1},{5751,1}}};var s=new FarmSession(h);Assert(s.Start(p,[Vector3.Zero],0));s.Tick(0);
            h.Seen[0]=h.Seen[0] with{Hp=0};h.Ctx=h.Ctx with{Combat=true};s.Tick(1000);
            h.Seen=[new(2,5750,Vector3.Zero,100,100)];s.Tick(2000);
            Assert(!h.Actions.Contains("focus:2") && !h.Actions.Contains("mount") && h.Actions.Contains("combat:off"));
        });
        Check("shared cave counters survive child-session stop and restart with no double deaths",()=>{
            var h=new Fake();var progress=new FarmProgress();var p=profile with{Goals=new Dictionary<uint,int>{{5750,1},{5751,2}}};progress.Reset(p);
            var s=new FarmSession(h,progress){MountBetweenTargets=false};h.Seen=[new(1,5750,h.Ctx.Position,100,100)];Assert(s.Start(p,[h.Ctx.Position],0));
            progress.Observe(h.Seen);s.Tick(0);h.Seen[0]=h.Seen[0] with{Hp=0};progress.Observe(h.Seen);s.Tick(1000);s.Stop();
            Assert(s.Deaths[5750]==1);progress.ForgetVisible();Assert(s.Start(p,[h.Ctx.Position],2000));s.Tick(2000);
            Assert(progress.Done(5750) && s.Deaths[5750]==1 && !s.Completed);
        });
        Check("whole cave patrol stops on quotas before departing for another cave",()=>{
            var h=new Fake();var r=new RubyPatrol(h){MountBetweenTargets=false};var p=profile with{Goals=new Dictionary<uint,int>{{5750,1},{5751,1}}};
            Assert(r.Start(p,[true,true,true],0));r.Tick(0);h.Seen=[new(1,5750,h.Ctx.Position,100,100),new(2,5751,h.Ctx.Position,100,100)];r.Tick(3000);
            h.Seen=h.Seen.Select(m=>m with{Hp=0}).ToList();r.Tick(5000);var actions=h.Actions.Count;r.Tick(7000);
            Assert(r.Completed && !r.Running && r.CompletedVisits==0 && actions==h.Actions.Count && r.Deaths.Values.Sum()==2);
        });
        Check("quota notifications are queued once and observation reset does not reset progress",()=>{
            var progress=new FarmProgress();progress.Reset(profile with{Goals=new Dictionary<uint,int>{{5750,1},{5751,1}}});
            var live=new FarmMob(1,5750,Vector3.Zero,100,100);progress.Observe([live]);progress.Observe([live with{Hp=0}]);
            Assert(progress.TakeCompletedKinds().SequenceEqual([5750u]));progress.Observe([live with{Hp=0}]);Assert(progress.TakeCompletedKinds().Length==0);
            progress.ForgetVisible();Assert(progress.Done(5750));progress.Observe([live with{Hp=0}]);Assert(progress.Deaths[5750]==1);
        });
        Check("unchecked kinds never gate the selected task's completion",()=>{
            var p=FarmProfiles.Profile(0,[true,false]);var progress=new FarmProgress();progress.Reset(p);
            for(var i=0;i<100;i++){var mob=new FarmMob((ulong)(i+1),5750,Vector3.Zero,100,100);progress.Observe([mob]);progress.Observe([mob with{Hp=0}]);}
            Assert(progress.Completed && progress.Deaths[5750]==100 && !progress.Deaths.ContainsKey(5751));
        });
        foreach(var provider in Enum.GetValues<RotationProvider>())
        Check($"{provider} completes mounted three-cave task, skips first quota, and releases once",()=>{
            var h=new Fake{Engage=provider==RotationProvider.AE?25:2.5f};h.Ctx=h.Ctx with{Position=RubyCaves.All[0].Interior};
            var api=new RotationChecks.FakeRotationApi();using var rotation=new CombatRotationSession(api);
            rotation.Begin(provider,true);h.CombatAction=rotation.SetCombat;
            var r=new RubyPatrol(h);Assert(r.Start(profile with{Goals=new Dictionary<uint,int>{{5750,2},{5751,2}}},[true,true,true],0));
            var spawned=new HashSet<int>();var completed=new List<uint>();var actionCursor=0;var moveCursor=0;ulong focused=0;var enabled=false;
            (Vector3 Position,bool Diving)? fade=null;
            for(long now=0;now<250000 && r.Running;now+=2000)
            {
                if(r.Phase==RubyPhase.Farm && spawned.Add(r.CaveIndex))
                {
                    var at=RubyCaves.All[r.CaveIndex].ScanPoint;
                    h.Seen=r.CaveIndex switch
                    {
                        0=>[new(1,5750,at+new Vector3(10,0,0),100,100)],
                        1=>[new(2,5750,at+new Vector3(10,0,0),100,100),new(3,5751,at+new Vector3(40,0,0),100,100)],
                        _=>[new(4,5751,at+new Vector3(10,0,0),100,100),new(5,5750,at,100,100)]
                    };
                }
                if(h.Ctx.Ready)Assert(rotation.CheckWhileReady()==null);
                r.Tick(now);completed.AddRange(r.Progress.TakeCompletedKinds());
                if(fade is { } loaded)
                {
                    h.Ctx=h.Ctx with{Ready=true,Loading=false,Position=loaded.Position,Diving=loaded.Diving,Mounted=loaded.Diving,Flying=false};fade=null;
                }
                foreach(var action in h.Actions.Skip(actionCursor).ToArray())
                {
                    if(action=="mount")h.Ctx=h.Ctx with{Mounted=true};
                    if(action=="takeoff")h.Ctx=h.Ctx with{Flying=true};
                    if(action=="dismount")h.Ctx=h.Ctx with{Mounted=false,Flying=false};
                    if(action=="clear")focused=0;
                    if(action=="combat:on")enabled=true;
                    if(action=="combat:off")enabled=false;
                    if(action.StartsWith("focus:"))focused=ulong.Parse(action[6..]);
                }
                actionCursor=h.Actions.Count;
                foreach(var move in h.Moves.Skip(moveCursor).ToArray())
                {
                    if(move.Direct)
                    {
                        var cave=RubyCaves.All[r.CaveIndex];var enter=r.Phase==RubyPhase.Enter;
                        fade=(enter?cave.Interior:cave.Exterior,!enter);h.Ctx=h.Ctx with{Ready=false,Loading=true};
                    }
                    else h.Ctx=h.Ctx with{Position=move.Point};
                    h.NavBusy=false;
                }
                moveCursor=h.Moves.Count;
                if(enabled && focused!=0)
                {
                    Assert(!h.Ctx.Mounted && !h.Ctx.Flying,"Combat cannot run while mounted");
                    var at=h.Seen.FindIndex(m=>m.Id==focused && m.Hp>0);
                    if(at>=0){Assert(!r.Progress.Done(h.Seen[at].NameId),"Completed kind was targeted again");h.Seen[at]=h.Seen[at] with{Hp=0};}
                }
            }
            Assert(r.Completed && !r.Running,r.State);Assert(r.Deaths[5750]==2 && r.Deaths[5751]==2);
            Assert(completed.Order().SequenceEqual(new[]{5750u,5751u}));
            Assert(h.Actions.Count(a=>a=="mount")>=4 && h.Moves.Where((m,i)=>h.MoveContexts[i].Diving).All((m)=>m.Fly));
            Assert(h.MoveContexts.Where(c=>c.Diving).All(c=>c.Mounted));
            Assert(h.MoveContexts.Where((c,i)=>h.Moves[i].Direct).All(c=>c.Mounted),"A cave portal was crossed unmounted");
            rotation.End();Assert(!rotation.Begun && !api.RsrActive && !api.PromeActive);
            if(provider==RotationProvider.WrathCombo)Assert(api.Count(RotationCall.WrathRelease)==1);
        });
        return count;
    }
}
