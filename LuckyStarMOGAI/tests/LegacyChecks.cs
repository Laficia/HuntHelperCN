using LuckyStar;
using ECommons.DalamudServices;
using System.Numerics;

internal static class LegacyChecks
{
    public static int Run()
    {
        var count=0;
        void Check(string name,Action test){LegacyRuntime.Reset();test();count++;Console.WriteLine("PASS original patrol: "+name);}
        static void Assert(bool value){if(!value)throw new Exception("original patrol assertion failed");}
        static void Tick()=>Svc.Framework.Tick();
        static List<string> Flights()=>LegacyRuntime.Commands.Where(c=>c.StartsWith("/vnav flyto ")).ToList();
        Check("AE retains the original on, mode7, pull and stop commands",()=>{
            using var c=new LegacyFarmController();c.Start([(10,0,0)]);
            Assert(LegacyRuntime.Commands.SequenceEqual(["/aeTargetSelector on","/aeTargetSelector mode7","/aepull on"]));
            c.Stop();Assert(LegacyRuntime.Commands.TakeLast(3).SequenceEqual(["/vnav stop","/aeTargetSelector off","/aepull off"]));
        });
        Check("flight uses the exact preset XYZ and dismount uses horizontal distance despite the height difference",()=>{
            using var c=new LegacyFarmController();c.Start([(10,20,30)]);Tick();Tick();
            Assert(Flights().Single()=="/vnav flyto 10 20 30");
            Svc.Objects.LocalPlayer!.Position=new(12,24,30);Svc.Objects.Add(new LegacyMob(113,new(10,20,30),false));Tick();
            Assert(LegacyRuntime.Actions.Last()==23 && Flights().Count==1 && c.PosDistance==2);
        });
        Check("a roaming mob never redirects the preset route or creates a nearby landing point",()=>{
            using var c=new LegacyFarmController();c.Start([(10,0,0)]);Tick();Tick();
            Svc.Objects.LocalPlayer!.Position=new(12,0,0);Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted]=false;
            foreach(var offset in new[]{5,10,15}){Svc.Objects.Clear();Svc.Objects.Add(new LegacyMob(113,new(10+offset,0,0),false));Tick();}
            Assert(Flights().Count==1 && c.DataIndex==0 && c.State=="等待击杀当前小怪");
        });
        Check("an empty completed point advances and flies directly to the next original point",()=>{
            using var c=new LegacyFarmController();c.Start([(10,0,0),(30,5,0)]);Tick();Tick();Svc.Objects.LocalPlayer!.Position=new(10,4,0);Tick();
            Assert(c.DataIndex==1 && c.NeedToTakeOff && c.ReadyToTheNextPos);Tick();Tick();
            Assert(Flights().SequenceEqual(["/vnav flyto 10 0 0","/vnav flyto 30 5 0"]));
        });
        Check("round completion retains the original first-spawn wait without an added ten-second delay",()=>{
            using var c=new LegacyFarmController();c.Start([(10,0,0)]);Tick();Tick();Svc.Objects.LocalPlayer!.Position=new(10,0,0);Tick();Tick();Tick();Tick();Tick();
            Assert(c.WaitingFirst && c.DataIndex==0 && c.State=="等待第一只刷新");var routes=Flights().Count;
            for(var i=0;i<10;i++)Tick();Assert(Flights().Count==routes);
            Svc.Objects.Add(new LegacyMob(113,new(10,0,0),false));Tick();Assert(!c.WaitingFirst && LegacyRuntime.Actions.Last()==23);
        });
        foreach(var provider in new[]{RotationProvider.RotationSolverReborn,RotationProvider.WrathCombo,RotationProvider.Prome})
            Check($"{provider} replaces rotation calls without changing the original movement path",()=>{
                var api=new RotationChecks.FakeRotationApi();using var rotation=new CombatRotationSession(api);rotation.Begin(provider,true);
                using var c=new LegacyFarmController();var focus=0;c.SetRotationCallbacks(rotation.SetCombat,()=>focus++);c.Start([(10,0,0)]);
                Tick();Tick();Svc.Objects.LocalPlayer!.Position=new(12,4,0);Svc.Objects.Add(new LegacyMob(113,new(10,0,0),false));Tick();
                Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted]=false;Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InFlight]=false;Tick();
                Assert(focus==1 && Flights().Single()=="/vnav flyto 10 0 0" && !LegacyRuntime.Commands.Any(c=>c.StartsWith("/ae")));
                c.Stop();rotation.End();Assert(!rotation.Begun);
            });
        return count;
    }
}
