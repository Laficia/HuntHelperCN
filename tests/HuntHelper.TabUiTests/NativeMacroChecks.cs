using HuntHelper.Gui;
using HuntHelper.Gui.Native;
using HuntHelper.Utilities;

internal static class NativeMacroChecks
{
    internal static void Run()
    {
        var settings = new HuntPointMacroSettings();
        var saves = 0; var sends = new List<(string Text, string Rank)>(); var legacy = 0;
        var ui = new HuntPointMacroUI(settings, () => saves++, () => false, () => "", () => { },
            (text, rank) => sends.Add((text, rank)), rank => "来源 " + rank) { NativePresentation = true, OpenLegacyUi = () => legacy++ };
        ui.Open("S"); var snapshot = ui.NativeSnapshot();
        Check(snapshot.Visible && saves == 0 && sends.Count == 0);
        Console.WriteLine("PASS Native macro snapshot opens without saving or sending and retains HHS context");
        Check(!snapshot.Rows.Any(r => r.Key == "variables"));
        ui.NativeDispatch(new("variables"));
        Check(ui.NativeSnapshot().Rows.Any(r => r.Key == "variables") && saves == 0 && sends.Count == 0);
        ui.NativeDispatch(new("variables"));
        Check(!ui.NativeSnapshot().Rows.Any(r => r.Key == "variables"));
        Console.WriteLine("PASS Compact native macro help starts collapsed and toggles without saving or sending");
        ui.NativeDispatch(new("text:s", "/sh <name><flag> <wait.2>\n/echo 中文 <sever>"));
        ui.NativeDispatch(new("send", "s"));
        Check(sends.Single() == (HuntPointMacroSettings.DefaultTemplate, "S") && saves == 0);
        ui.NativeDispatch(new("save")); ui.NativeDispatch(new("send", "s"));
        Check(sends.Last() == (settings.S, "S") && settings.S.Contains("\n") && settings.S.Contains("<wait.2>"));
        Console.WriteLine("PASS Native macro sends saved text only and preserves multiline wait commands and aliases");
        ui.NativeDispatch(new("add")); var custom = settings.Custom.Single().Id;
        ui.NativeDispatch(new("name:" + custom, "测试宏")); ui.NativeDispatch(new("text:" + custom, "/echo 自定义")); ui.NativeDispatch(new("save"));
        ui.NativeDispatch(new("pin", custom, "a", false));
        Check(settings.Collection[0] == custom && settings.Custom[0].Text == "/echo 自定义" && sends.Count == 2);
        ui.NativeDispatch(new("pin", custom, "s", true)); Check(settings.Collection.Last() == custom);
        Console.WriteLine("PASS Native macro page edits and forward or backward collection drops never send");
        ui.NativeDispatch(new("text:" + custom, "未保存草稿")); ui.NativeDispatch(new("legacy"));
        Check(legacy == 1 && ui.NativeSnapshot().Rows.Single(r => r.Key == "text:" + custom).Value == "未保存草稿");
        Console.WriteLine("PASS Native to legacy presentation switching retains the unsaved editor draft");
        ui.NativeDispatch(new("delete", "a")); ui.NativeDispatch(new("delete-confirm")); Check(settings.Collection.Contains("a"));
        ui.NativeDispatch(new("delete", custom)); Check(settings.Custom.Count == 1);
        ui.NativeDispatch(new("delete-cancel")); Check(settings.Custom.Count == 1);
        ui.NativeDispatch(new("delete", custom)); ui.NativeDispatch(new("delete-confirm"));
        Check(settings.Custom.Count == 0 && !settings.Collection.Contains(custom));
        Console.WriteLine("PASS Native custom macro deletion requires confirmation and cannot delete built-in presets");
        ui.NativeDispatch(new("close")); Check(!ui.NativeSnapshot().Visible);
    }
    private static void Check(bool value) { if (!value) throw new Exception("Native macro model contract failed"); }
}
