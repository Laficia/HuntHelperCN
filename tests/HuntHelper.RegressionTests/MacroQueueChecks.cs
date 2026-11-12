using Dalamud.Plugin.Services;
using HuntHelper;
using HuntHelper.Managers;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;

internal static class MacroQueueChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("Actual S macro manager submits shout input and the foreign record flag through the native boundary", Shout),
        ("A coordinate submission uses its independent saved template", ARank),
        ("Queued lines retain their record and template snapshots with paced submission", Snapshots),
        ("An unavailable map flag cancels outgoing chat rather than emitting a local substitute", FailedFlag),
        ("Plain custom reminders send with no selected record while variable macros refuse", Plain),
        ("Busy repeated clicks do not create a second outgoing batch", Busy),
        ("Logout world transfer cancellation and disposal discard remaining messages", Cancellation),
        ("Only explicitly selected echo channels are reported as local output", Echo),
    ];
    private sealed class Framework : IFramework
    { public event Action<IFramework>? Update; public void Tick() => Update?.Invoke(this); }
    private sealed class Client : IClientState { public bool IsLoggedIn { get; set; } = true; }
    private sealed class Player : IPlayerState
    { public bool IsLoaded { get; set; } = true; public TestWorldReference CurrentWorld { get; set; } = new(101, null); }
    private sealed class Host : IHuntPointChatHost
    {
        public readonly List<(string Text, HuntPointChatFlag? Flag)> Submitted = new();
        public HuntPointChatFlag? Flag; public bool FailFlag;
        public IDisposable UseFlag(HuntPointChatFlag flag)
        { if (FailFlag) throw new InvalidOperationException("No native map agent"); var previous = Flag; Flag = flag; return new Restore(() => Flag = previous); }
        public void SendText(string text) => Submitted.Add((text, Flag));
        private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
    }
    private sealed class Fixture : IDisposable
    {
        public readonly Configuration Config = new(); public readonly Framework Framework = new();
        public readonly Client Client = new(); public readonly Player Player = new(); public readonly Host Host = new();
        public long Now; public readonly HuntPointMacroManager Sender;
        public Fixture() => Sender = new(Config, Player, Client, Framework, Host, () => Now);
        public void Dispose() => Sender.Dispose();
    }
    private static HuntTrainMob Mob() => new("测试S怪", 1, 134, 30, 2, "地图", new(18, 12), DateTime.UtcNow, worldId: 102, worldName: "水晶塔");
    private static void Check(bool ok) { if (!ok) throw new Exception("Macro native submission assertion failed"); }
    private static void Shout()
    {
        using var f = new Fixture(); f.Sender.Send(Mob(), "S"); Check(f.Host.Submitted.Count == 0); f.Framework.Tick();
        var sent = f.Host.Submitted.Single();
        Check(sent.Text == "/sh 下一站【水晶塔】测试S怪<flag>" && sent.Flag is { TerritoryId: 134, MapId: 30, X: 18, Z: 12 });
        Check(f.Host.Flag == null && !f.Sender.Busy && !f.Sender.Status.Contains("本地回显"));
    }
    private static void ARank()
    {
        using var f = new Fixture(); f.Config.PointMacros.A = "/sh A <name><flag>"; f.Sender.Send(Mob(), "A"); f.Framework.Tick();
        Check(f.Host.Submitted.Single().Text == "/sh A 测试S怪<flag>");
    }
    private static void Snapshots()
    {
        using var f = new Fixture(); var mob = Mob(); f.Config.PointMacros.S = "/sh <name><flag>\n/sh <sever><flag>";
        f.Sender.Send(mob, "S"); mob.Name = "changed"; mob.Position = new(99, 99); f.Config.PointMacros.S = "/echo changed";
        f.Framework.Tick(); f.Now = 1199; f.Framework.Tick(); Check(f.Host.Submitted.Count == 1 && f.Sender.Busy);
        f.Now = 1200; f.Framework.Tick(); Check(f.Host.Submitted.Count == 2 && !f.Sender.Busy);
        Check(f.Host.Submitted[0].Text == "/sh 测试S怪<flag>" && f.Host.Submitted[1].Text == "/sh 水晶塔<flag>" && f.Host.Submitted.All(s => s.Flag?.X == 18));
    }
    private static void FailedFlag()
    {
        using var f = new Fixture(); f.Host.FailFlag = true; f.Sender.Send(Mob(), "A"); f.Framework.Tick();
        Check(f.Host.Submitted.Count == 0 && !f.Sender.Busy && f.Sender.Status.Contains("发送失败"));
    }
    private static void Plain()
    {
        using var f = new Fixture(); f.Sender.SendTemplate("/sh 请等待倒计时", null, "A"); f.Framework.Tick();
        Check(f.Host.Submitted.Single().Text == "/sh 请等待倒计时" && f.Host.Submitted.Single().Flag == null);
        f.Sender.SendTemplate("/sh <flag>", null, "A"); Check(!f.Sender.Busy && f.Sender.Status.Contains("选中怪物"));
    }
    private static void Busy()
    { using var f = new Fixture(); f.Sender.Send(Mob(), "A"); f.Sender.Send(Mob(), "S"); f.Framework.Tick(); Check(f.Host.Submitted.Count == 1); }
    private static void Cancellation()
    {
        foreach (var action in new[] { "logout", "world", "cancel", "dispose" })
        {
            using var f = new Fixture(); f.Sender.SendTemplate("/sh first\n/sh second", null, "A"); f.Framework.Tick();
            if (action == "logout") f.Client.IsLoggedIn = false;
            else if (action == "world") f.Player.CurrentWorld = new(102, null);
            else if (action == "cancel") f.Sender.Cancel(); else f.Sender.Dispose();
            f.Now = 5000; f.Framework.Tick(); Check(f.Host.Submitted.Count == 1 && !f.Sender.Busy);
        }
    }
    private static void Echo()
    {
        using var f = new Fixture(); f.Sender.SendTemplate("/echo preview", null, "A"); f.Framework.Tick();
        Check(f.Sender.Status.Contains("仅自己可见") && f.Host.Submitted.Single().Text == "/echo preview");
    }
}

namespace Dalamud.Plugin.Services
{
    public interface IFramework { event Action<IFramework> Update; }
}
namespace HuntHelper.Managers
{
    // No game functions are loaded or called by this runner; explicit host injection is mandatory.
    public sealed class GameHuntPointChatHost : IHuntPointChatHost
    {
        public IDisposable UseFlag(HuntPointChatFlag _) => throw new Exception("Unexpected game access");
        public void SendText(string _) => throw new Exception("Unexpected game access");
    }
}
