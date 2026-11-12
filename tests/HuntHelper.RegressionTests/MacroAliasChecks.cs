using System.Numerics;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;

internal static class MacroAliasChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("Requested English aliases expand identically to Chinese for name map rank world and instance", Aliases),
        ("English aliases retain local-world substitution and are case insensitive", LocalAndCase),
        ("Removed x and y variables fail clearly before a message can be submitted", RemovedCoordinates),
        ("A and S share one default and migrate only legacy built-ins once", Defaults),
    ];
    private static HuntTrainMob Mob(uint line = 2) => new("女王蜂", 1, 134, 30, line, "基拉巴尼亚边区", new Vector2(20, 25),
        DateTime.UtcNow, worldId: 102, worldName: "水晶塔");
    private static string Expand(string template, uint current = 101, uint line = 2, string rank = "A") =>
        HuntPointChatDelivery.Build(HuntPointMacro.Expand(template, Mob(line), rank, current)).Single().Text;
    private static void Check(bool value) { if (!value) throw new Exception("Macro alias/default assertion failed"); }
    private static void Aliases()
    {
        const string chinese = "/sh <服务器>|<名字>|<地图>|<等级>|<分线><flag>";
        const string english = "/sh <sever>|<name>|<map>|<lv>|<ins><flag>";
        Check(Expand(chinese) == Expand(english));
        Check(Expand(english) == "/sh 水晶塔|女王蜂|基拉巴尼亚边区|A|2线<flag>");
        Check(Expand(english, rank: "S").Contains("|S|2线<flag>"));
        Check(Expand("/sh <world>|<rank>|<instance>") == "/sh 水晶塔|A|2线");
    }
    private static void LocalAndCase()
    {
        Check(Expand("/sh <SEVER>|<NAME>|<MAP>|<LV>|<INS>", current: 102, line: 0) == "/sh 本地|女王蜂|基拉巴尼亚边区|A|");
    }
    private static void RemovedCoordinates()
    {
        foreach (var template in new[] { "/sh <x>", "/sh <y>", "/sh <X>,<Y><flag>" })
        {
            var rejected = false;
            try { Expand(template); }
            catch (ArgumentException error) { rejected = error.Message.Contains("已取消") && error.Message.Contains("<flag>"); }
            Check(rejected);
        }
    }
    private static void Defaults()
    {
        const string expected = "/sh 下一站【<服务器>】<名字><flag>";
        var fresh = new HuntPointMacroSettings(); Check(fresh.A == expected && fresh.S == expected);
        var old = new HuntPointMacroSettings
        {
            A = "/p 下一站【<服务器>】<名字><flag>",
            S = "/sh 下一站【<服务器>】S怪<flag>，请等待触发倒计时/定，不要抢开！"
        };
        Check(old.UpgradeLegacyDefaults() && old.A == expected && old.S == expected);
        old.A = "/p 下一站【<服务器>】<名字><flag>";
        Check(!old.UpgradeLegacyDefaults() && old.A.StartsWith("/p"));
        var custom = new HuntPointMacroSettings { A = "/p 自定义<name><flag>", S = "/sh <sever> <lv><name><flag>" };
        var a = custom.A; var s = custom.S;
        Check(custom.UpgradeLegacyDefaults() && custom.A == a && custom.S == s);
    }
}
