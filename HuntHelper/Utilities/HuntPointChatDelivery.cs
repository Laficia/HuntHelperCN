using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HuntHelper.Utilities;

public sealed record HuntPointChatLine(string Text, bool UsesFlag, string Channel, bool LocalOnly, int WaitSeconds = 0)
{
    public bool IsWaitOnly => Text.Length == 0 && WaitSeconds > 0;
}
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
            var waits = parts.Where(p => p.WaitSeconds != 0).ToArray();
            if (waits.Length > 1 || waits.Any(p => p.WaitSeconds is < 1 or > 60 || p.IsFlag || p.Text.Length != 0))
                throw new ArgumentException("宏的本地等待步骤无效");
            var waitSeconds = waits.Length == 0 ? 0 : waits[0].WaitSeconds;
            if (parts.Any(p => !p.IsFlag && (p.Text.Contains("<flag>", StringComparison.OrdinalIgnoreCase) ||
                p.Text.Contains("<pos>", StringComparison.OrdinalIgnoreCase))))
                throw new ArgumentException("记录名称等数据中含有点位指令，请修正记录后重试");
            var text = string.Concat(parts.Select(p => p.IsFlag ? "<flag>" : p.Text));
            if (HuntPointMacro.ContainsWaitMarker(text))
                throw new ArgumentException("记录名称等数据中含有等待标记，请修正记录后重试");
            if (text.Length == 0 && waitSeconds > 0)
                return new HuntPointChatLine("", false, "", false, waitSeconds);
            if (text.Any(char.IsControl) || Encoding.UTF8.GetByteCount(text) is 0 or > 500)
                throw new ArgumentException("展开后的单行宏须为有效文本且不超过 500 字节");
            var channel = text.Split(' ', 2)[0].ToLowerInvariant();
            return new HuntPointChatLine(text, parts.Any(p => p.IsFlag), channel, channel is "/echo" or "/e", waitSeconds);
        }).ToArray();

    public static void Send(HuntPointChatLine line, HuntPointChatFlag flag, IHuntPointChatHost host)
    {
        if (line.IsWaitOnly) return;
        if (HuntPointMacro.ContainsWaitMarker(line.Text))
            throw new ArgumentException("等待标记只能由本地宏队列执行，不能发送到聊天");
        // The parser resolves <flag> synchronously against this record's temporary flag.
        // Restore the previous flag even if submission fails; never use a stale/player flag.
        using var lease = line.UsesFlag ? host.UseFlag(flag) : null;
        host.SendText(line.Text);
    }
}
