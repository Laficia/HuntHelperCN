using System.Text.RegularExpressions;

namespace HuntHelper.Utilities;

/// <summary>Only explicit terminal death statuses, not history or arbitrary mentions of death.</summary>
public static class SonarKillMessage
{
    private static readonly Regex Status = new(@"(?<=[>＞])\s*(?:was just killed|DEAD|已被击杀|已击杀|已死亡|已被讨伐|已讨伐)[.!。！]?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    public static bool TryStrip(string afterLink, out string location)
    {
        var match = Status.Match(afterLink);
        location = match.Success ? afterLink[..match.Index] : afterLink;
        return match.Success;
    }
}
