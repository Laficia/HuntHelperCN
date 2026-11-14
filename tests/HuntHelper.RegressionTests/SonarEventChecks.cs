using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper;
using HuntHelper.Managers;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;

internal static class SonarEventChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("Actual Sonar chat event marks A dead without requiring A recording", () => Test()),
        ("Disabled Sonar kill setting leaves existing records alive", () => Test(enabled: false)),
        ("Untagged player text cannot spoof a Sonar killed announcement", () => Test(sender: "玩家")),
        ("S killed messages never mark an A record with the same numeric id", () => Test(rank: "S")),
        ("Sonar kill preserves other servers and reports an explicit world mismatch", () => Test(world: "水晶塔")),
    ];
    private sealed class Host : IChatGui, IGameGui
    {
        public event Action<IHandleableChatMessage>? ChatMessage;
        public void Raise(IHandleableChatMessage message) => ChatMessage?.Invoke(message);
        public void Print(SeString _) => throw new Exception("Unexpected outgoing chat");
        public void OpenMapWithMapLink(MapLinkPayload _) => throw new Exception("Unexpected map open");
    }
    private sealed record RelayMessage(SeString Sender, SeString MessageBody) : IHandleableChatMessage
    { public SeString Message => MessageBody; }
    private sealed class Client : IClientState { public bool IsLoggedIn => true; }
    private sealed class Data : IDataManager
    { public TestSheet<T> GetExcelSheet<T>() where T : struct => new(); }
    private static void Test(bool enabled = true, string sender = "Sonar", string rank = "A", string world = "银泪湖")
    {
        using var dir = new TemporaryDirectory(); var host = new Host();
        using var a = new TrainManager(host, host, Path.Combine(dir.Path, "a.json"), () => new HuntTrainWorld(101, "银泪湖", "", 0));
        using var s = new TrainManager(host, host, Path.Combine(dir.Path, "s.json"), () => new HuntTrainWorld(101, "银泪湖", "", 0));
        a.AddMob(new("女王蜂", 1, 134, 30, 0, "地图", new(20, 20), DateTime.UtcNow.AddMinutes(-1), worldId: 101, worldName: "银泪湖"));
        var config = new Configuration { SonarSyncAKills = enabled };
        var hunts = new HuntManager();
        using var recorder = new SonarChatRecorder(host, new Client(), new Data(), hunts, a, s, config);
        var message = new SeString(); message.Payloads.Add(new TextPayload($"Rank {rank}: 女王蜂 "));
        message.Payloads.Add(new MapLinkPayload(134, 30, 20, 20));
        message.Payloads.Add(new TextPayload($" <{world}> was just killed"));
        host.Raise(new RelayMessage(new SeString { TextValue = sender }, message));
        var expected = enabled && sender == "Sonar" && rank == "A" && world == "银泪湖";
        if (a.HuntTrain.Single().Dead != expected || s.GetAllTrainRecords().Count != 0 || a.RecordTrain)
            throw new Exception("Sonar chat event routing failed");
        if (expected && !recorder.AStatus.Contains("已标记死亡")) throw new Exception("Missing kill sync status");
    }
}

namespace Dalamud.Plugin.Services
{
    public interface IClientState { bool IsLoggedIn { get; } }
    public interface IDataManager { TestSheet<T> GetExcelSheet<T>() where T : struct; }
    public sealed class TestSheet<T> where T : struct
    {
        public T? GetRowOrDefault(uint _) => typeof(T) == typeof(Lumina.Excel.Sheets.Map) ? (T)(object)new Lumina.Excel.Sheets.Map() : null;
    }
}
namespace Lumina.Excel.Sheets
{
    public struct Map { public TestRow TerritoryType => new(134); public uint SizeFactor => 100; }
}
namespace HuntHelper.Managers.Hunts
{
    public sealed class HuntManager
    {
        public IReadOnlyList<HuntHelper.Utilities.HuntRelaySpecies> RelaySpecies => new[] { new HuntHelper.Utilities.HuntRelaySpecies("A", 1, "女王蜂", "Queen Hawk", 134, 7), new HuntHelper.Utilities.HuntRelaySpecies("S", 2, "测试S", "TestS", 134, 7) };
    }
}
