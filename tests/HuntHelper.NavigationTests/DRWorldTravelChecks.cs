using HuntHelper.Managers.Automation;
using HuntHelper.Managers.Hunts.Models;

namespace HuntHelper.NavigationTests;

internal static class DRWorldTravelChecks
{
    private sealed class Fixture
    {
        internal readonly FakePlugin Plugin = new();
        internal readonly List<string> Commands = new();
        internal readonly DRWorldTravel Travel;
        internal uint World = 101;
        internal bool Ready = true, Waiting, Accept = true;
        internal long Now = 1000;
        internal Fixture()
        {
            Plugin.At("DailyRoutines.IsModuleEnabled").Function = (Func<string, bool?>)(name => name == "FastWorldTravel");
            Travel = new(Plugin, text => { Commands.Add(text); return Accept; },
                () => (World, Ready), () => Waiting, () => "豆豆柴", () => Now);
        }
    }
    private static readonly HuntTrainWorld World = new(102, "银泪湖", "豆豆柴", 0);
    private static void Check(bool ok) { if (!ok) throw new Exception("DR travel assertion failed"); }
    internal static void Run(Action<string, Action> run)
    {
        run("DR requests FastWorldTravel by exact numeric ID without any Lifestream IPC", () => {
            var f = new Fixture(); Check(f.Travel.Change(World, out _));
            Check(f.Commands.SequenceEqual(new[] { "/pdr worldtravel 102" }) && f.Travel.Busy);
            Check(f.Plugin.At("Lifestream.ChangeWorldById").Calls == 0);
        });
        run("DR missing disabled unknown and broken module queries never dispatch travel", () => {
            foreach (var state in new bool?[] { false, null }) {
                var f = new Fixture(); f.Plugin.At("DailyRoutines.IsModuleEnabled").Function = (Func<string, bool?>)(_ => state);
                Check(!f.Travel.Change(World, out var error) && error.Contains("快捷跨服") && f.Commands.Count == 0);
            }
            var missing = new Fixture(); missing.Plugin.Available = false;
            Check(!missing.Travel.Change(World, out _) && missing.Commands.Count == 0);
            var broken = new Fixture(); broken.Plugin.At("DailyRoutines.IsModuleEnabled").Function = (Func<string, bool?>)(_ => throw new Exception());
            Check(!broken.Travel.Change(World, out _) && broken.Commands.Count == 0);
        });
        run("DR CN cross-DC preflight requires the actual DCTravelerX validity gate", () => {
            var f = new Fixture(); var cross = new HuntTrainWorld(103, "水晶塔", "陆行鸟", 0);
            Check(!f.Travel.Change(cross, out var error) && error.Contains("DCTravelerX") && f.Commands.Count == 0);
            f.Plugin.At("DCTravelerX.IsValid").Function = (Func<bool>)(() => true);
            Check(f.Travel.Change(cross, out _) && f.Commands.Single() == "/pdr worldtravel 103");
        });
        run("DR waits through slow city teleport logout and intermediate worlds then stable arrival", () => {
            var f = new Fixture(); Check(f.Travel.Change(World, out _));
            f.Now += 90000; Check(f.Travel.Busy);
            f.Ready = false; f.World = 0; f.Now += 90000; Check(f.Travel.Busy);
            f.Ready = true; f.World = 103; Check(f.Travel.Busy);
            f.World = 102; Check(f.Travel.Busy);
            f.Now += 1500; Check(f.Travel.Busy);
            f.Ready = false; Check(f.Travel.Busy); f.Ready = true; Check(f.Travel.Busy);
            f.Now += 2000; Check(!f.Travel.Busy && f.Commands.Count == 1);
        });
        run("DR does not dispatch twice while waiting or claim a refused command was accepted", () => {
            var f = new Fixture { Accept = false }; Check(!f.Travel.Change(World, out _) && !f.Travel.Busy);
            f.Accept = true; Check(f.Travel.Change(World, out _) && !f.Travel.Change(World, out _) && f.Commands.Count == 2);
            var waiting = new Fixture { Waiting = true }; Check(!waiting.Travel.Change(World, out _) && waiting.Commands.Count == 0);
        });
        run("DR stop releases our queue without inventing abort commands or unloading modules", () => {
            var f = new Fixture(); Check(f.Travel.Change(World, out _));
            f.Travel.ReleaseOwnedRequest(); Check(!f.Travel.Busy && f.Commands.Count == 1);
            Check(f.Plugin.At("DailyRoutines.UnloadModule").Calls == 0 && f.Plugin.At("Lifestream.Abort").Calls == 0);
        });
        run("Hunt teleport crosses to the recorded server, waits for stable arrival and sends its crystal once", () => {
            var f = new Fixture();
            var crystals = new List<uint>();
            var teleport = new HuntCrystalTravel(f.Travel, crystal => { crystals.Add(crystal); return true; });
            void Tick(bool canTeleport = true) => teleport.Tick(f.World, f.Ready, canTeleport, f.Now);
            Check(teleport.Begin(World, 207, f.Now) && !teleport.Begin(World, 208, f.Now));
            Tick();
            f.World = 103; f.Now += 5000; Tick();
            f.World = 102; f.Ready = false; f.Now += 5000; Tick();
            f.Ready = true; Tick(); f.Now += 1500; Tick();
            Check(teleport.Busy && crystals.Count == 0);
            f.Ready = false; Tick(); f.Ready = true; Tick();
            f.Now += 2500; Tick(false);
            Check(teleport.Busy && crystals.Count == 0);
            Tick(); Tick();
            Check(!teleport.Busy && f.Commands.Count == 1 && crystals.SequenceEqual(new uint[] { 207 }));
        });
        run("Refused, timed out and cancelled hunt world visits never start a crystal teleport", () => {
            var f = new Fixture { Accept = false };
            var teleport = new HuntCrystalTravel(f.Travel, _ => throw new Exception("Unexpected crystal teleport"));
            Check(!teleport.Begin(World, 207, f.Now) && !teleport.Busy && teleport.Status.Length > 0);
            f.Accept = true; Check(teleport.Begin(World, 207, f.Now));
            f.Now += 600000; teleport.Tick(f.World, f.Ready, true, f.Now);
            Check(!teleport.Busy && !f.Travel.Busy && teleport.Status.Contains("超时"));
            Check(teleport.Begin(World, 207, f.Now));
            teleport.Cancel("卸载");
            f.World = World.WorldID; f.Now += 3000; teleport.Tick(f.World, f.Ready, true, f.Now);
            Check(!teleport.Busy && teleport.Status == "卸载");
        });
    }
}
