using System.Numerics;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;

internal static class PointChatDeliveryChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("Shout input retains its channel and native flag placeholder instead of rendered payloads", ShoutInput),
        ("Delivery installs the record flag before native submission and restores the prior flag", FlagTransaction),
        ("Failed native submission restores the prior flag without falling back to local echo", FailedSend),
        ("Failed record flag setup prevents submission with a stale flag", FailedFlag),
        ("Echo remains explicitly local and no-flag lines leave the game flag alone", LocalAndPlain),
        ("All macro lines validate before any submission, including UTF8 byte limits", WholeBatchValidation),
        ("Record data cannot inject player-position or stale flag placeholders", InjectedData),
    ];
    private static readonly HuntPointChatFlag Original = new(134, 30, 80, 90), Target = new(1190, 789, -120, 230);
    private static HuntTrainMob Mob() => new("测试S怪", 1, 1190, 789, 0, "夏劳尼荒野", new Vector2(20, 25), DateTime.UtcNow,
        worldId: 102, worldName: "水晶塔");
    private static HuntPointChatLine Line(string template = "/sh 下一站【<服务器>】S怪<flag>") =>
        HuntPointChatDelivery.Build(HuntPointMacro.Expand(template, Mob(), "S", 101)).Single();
    private static void Check(bool ok) { if (!ok) throw new Exception("Outgoing point chat assertion failed"); }
    private sealed class Host : IHuntPointChatHost
    {
        public HuntPointChatFlag Current = Original;
        public readonly List<string> Events = new();
        public readonly List<(string Text, HuntPointChatFlag Flag)> Submitted = new();
        public bool FailFlag, FailSend;
        public IDisposable UseFlag(HuntPointChatFlag flag)
        {
            Events.Add("set"); if (FailFlag) throw new InvalidOperationException("No map agent");
            var old = Current; Current = flag; return new Restore(() => { Current = old; Events.Add("restore"); });
        }
        public void SendText(string text)
        {
            Events.Add("submit"); if (FailSend) throw new InvalidOperationException("Native error");
            Submitted.Add((text, Current));
        }
        private sealed class Restore(Action restore) : IDisposable { public void Dispose() => restore(); }
    }
    private static void ShoutInput()
    {
        var line = Line();
        Check(line.Text == "/sh 下一站【水晶塔】S怪<flag>" && line.Channel == "/sh" && !line.LocalOnly && line.UsesFlag);
        Check(!line.Text.Contains('\u0002') && !line.Text.Contains('\u0003'));
    }
    private static void FlagTransaction()
    {
        var host = new Host(); HuntPointChatDelivery.Send(Line(), Target, host);
        Check(host.Events.SequenceEqual(new[] { "set", "submit", "restore" }) && host.Current == Original);
        Check(host.Submitted.Single().Flag == Target && host.Submitted.Single().Text.EndsWith("<flag>"));
    }
    private static void FailedSend()
    {
        var host = new Host { FailSend = true }; var failed = false;
        try { HuntPointChatDelivery.Send(Line(), Target, host); } catch (InvalidOperationException) { failed = true; }
        Check(failed && host.Current == Original && host.Submitted.Count == 0 && host.Events.Last() == "restore");
    }
    private static void FailedFlag()
    {
        var host = new Host { FailFlag = true }; var failed = false;
        try { HuntPointChatDelivery.Send(Line(), Target, host); } catch (InvalidOperationException) { failed = true; }
        Check(failed && host.Current == Original && host.Submitted.Count == 0 && !host.Events.Contains("submit"));
    }
    private static void LocalAndPlain()
    {
        foreach (var command in new[] { "/echo", "/e", "/p", "/sh" })
        {
            var host = new Host(); var line = Line(command + " 正文"); HuntPointChatDelivery.Send(line, Target, host);
            Check(line.LocalOnly == (command is "/echo" or "/e") && host.Current == Original && host.Events.SequenceEqual(new[] { "submit" }));
        }
        Check(Line("/p <pos>").Text == "/p <flag>");
    }
    private static void WholeBatchValidation()
    {
        var failed = false; var host = new Host();
        try
        {
            var lines = HuntPointChatDelivery.Build(HuntPointMacro.Expand("/sh 第一行<flag>\n/sh " + new string('中', 170), Mob(), "S", 101));
            foreach (var line in lines) HuntPointChatDelivery.Send(line, Target, host);
        }
        catch (ArgumentException) { failed = true; }
        Check(failed && host.Submitted.Count == 0 && host.Events.Count == 0);
    }
    private static void InjectedData()
    {
        foreach (var token in new[] { "<pos>", "<flag>" })
        {
            var mob = Mob(); mob.Name = token; var failed = false;
            try { HuntPointChatDelivery.Build(HuntPointMacro.Expand("/sh <名字>", mob, "S", 101)); }
            catch (ArgumentException) { failed = true; }
            Check(failed);
        }
    }
}
