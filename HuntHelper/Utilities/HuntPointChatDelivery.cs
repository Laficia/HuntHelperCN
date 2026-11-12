using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HuntHelper.Utilities;

public sealed record HuntPointChatLine(string Text, bool UsesFlag, string Channel, bool LocalOnly);
public readonly record struct HuntPointChatFlag(uint TerritoryId, uint MapId, float X, float Z);

public interface IHuntPointChatHost
{
    IDisposable UseFlag(HuntPointChatFlag flag);
    void SendText(string text);
}

/// <summary>Send input text through the game's command parser. Never submit display SeString payloads.</summary>
public static class HuntPointChatDelivery
{
    public static IReadOnlyList<HuntPointChatLine> Build(IReadOnlyList<IReadOnlyList<HuntMacroPart>> lines) =>
        lines.Select(parts =>
        {
            if (parts.Any(p => !p.IsFlag && (p.Text.Contains("<flag>", StringComparison.OrdinalIgnoreCase) ||
                p.Text.Contains("<pos>", StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("记录名称等数据中含有点位指令，请修正记录后重试");
            var text = string.Concat(parts.Select(p => p.IsFlag ? "<flag>" : p.Text));
            if (text.Any(char.IsControl) || Encoding.UTF8.GetByteCount(text) is 0 or > 500)
                throw new ArgumentException("展开后的单行宏须为有效文本且不超过 500 字节");
            var channel = text.Split(' ', 2)[0].ToLowerInvariant();
            return new HuntPointChatLine(text, parts.Any(p => p.IsFlag), channel, channel is "/echo" or "/e");
        }).ToArray();

    public static void Send(HuntPointChatLine line, HuntPointChatFlag flag, IHuntPointChatHost host)
    {
        // The parser resolves <flag> synchronously against this record's temporary flag.
        // Restore the previous flag even if submission fails; never use a stale/player flag.
        using var lease = line.UsesFlag ? host.UseFlag(flag) : null;
        host.SendText(line.Text);
    }
}
