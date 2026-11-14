using System.Collections.Generic;
using Newtonsoft.Json;

namespace HuntHelper.Utilities;

public sealed class HuntPointMacroSettings
{
    public const string DefaultTemplate = "/sh 下一站【<服务器>】<名字><flag>";
    public string A { get; set; } = DefaultTemplate;
    public string S { get; set; } = DefaultTemplate;
    public string AName { get; set; } = "A 报点";
    public string SName { get; set; } = "S 报点";
    public List<HuntCustomMacro> Custom { get; set; } = new();
    [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
    public List<string> Collection { get; set; } = new() { "a", "s" };
    public int DefaultsVersion { get; set; }

    public bool UpgradeLegacyDefaults()
    {
        if (DefaultsVersion >= 1) return false;
        if (IsLegacyPreset(A)) A = DefaultTemplate;
        if (IsLegacyPreset(S)) S = DefaultTemplate;
        DefaultsVersion = 1;
        return true;
    }

    private static bool IsLegacyPreset(string text) => text is
        "/p 下一站【<服务器>】<名字><flag>" or
        "/p 下一站【<服务器>】S怪<flag>，请等待触发倒计时/定，不要抢开！" or
        "/sh 下一站【<服务器>】S怪<flag>，请等待触发倒计时/定，不要抢开！" or
        "/echo 【<服务器>】<等级>怪 <名字><flag><分线>";
}

public sealed class HuntCustomMacro
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "自定义宏";
    public string Text { get; set; } = HuntPointMacroSettings.DefaultTemplate;
}
