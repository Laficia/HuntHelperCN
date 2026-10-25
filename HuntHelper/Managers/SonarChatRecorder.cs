using Dalamud.Game.Chat;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts;
using HuntHelper.Utilities;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Managers;

/// <summary>Consumes displayed map announcements. No Sonar assembly reference or network subscription.</summary>
public sealed class SonarChatRecorder : IDisposable
{
    private readonly IChatGui _chat;
    private readonly IClientState _client;
    private readonly IDataManager _data;
    private readonly HuntManager _hunts;
    private readonly TrainManager _a, _s;
    private readonly Configuration _config;
    private readonly Dictionary<char, uint> _glyphs = new();
    private readonly HuntRelayWorldCatalog _worlds = new(MapHelpers.GetImportableWorlds);
    public string AStatus { get; private set; } = string.Empty;
    public string SStatus { get; private set; } = string.Empty;

    public SonarChatRecorder(IChatGui chat, IClientState client, IDataManager data, HuntManager hunts,
        TrainManager a, TrainManager s, Configuration config)
    {
        _chat=chat;_client=client;_data=data;_hunts=hunts;_a=a;_s=s;_config=config;
        for(uint i=1;i<=9;i++) foreach(var glyph in i.GetInstanceGlyph()) _glyphs[glyph]=i;
        _chat.ChatMessage += OnChat;
    }

    private void OnChat(IHandleableChatMessage message)
    {
        if (!_client.IsLoggedIn) return;
        string? rank = null;
        try
        {
            var payloads = message.Message.Payloads;
            var links = payloads.OfType<MapLinkPayload>().ToList();
            var index = links.Count == 0 ? payloads.Count : payloads.IndexOf(links[0]);
            var before = string.Concat(payloads.Take(index).OfType<TextPayload>().Select(p => p.Text));
            var after = string.Concat(payloads.Skip(index + 1).OfType<TextPayload>().Select(p => p.Text));
            var tagged = payloads.OfType<DalamudLinkPayload>().Any(p => p.Plugin.Contains("Sonar", StringComparison.OrdinalIgnoreCase)) ||
                before.Contains("[Sonar", StringComparison.OrdinalIgnoreCase);
            rank = HuntRelayParser.RankHint(before);
            if (rank is not ("A" or "S" or "SS") && (!tagged || rank != null)) return;
            if (rank == null && !_hunts.RelaySpecies.Any(s =>
                !string.IsNullOrWhiteSpace(s.Name) && before.Contains(s.Name, StringComparison.OrdinalIgnoreCase) ||
                !string.IsNullOrWhiteSpace(s.Alias) && before.Contains(s.Alias, StringComparison.OrdinalIgnoreCase))) return;
            if (rank == "SS") { Report("S", "未录入：目前仅支持 S 怪，不含 SS 及随从"); return; }
            if (rank is "A" or "S")
            {
                var target = rank == "A" ? _a : _s;
                var enabled = rank == "A" ? _config.HuntTrainCaptureSonar : _config.SRankTrain.HuntTrainCaptureSonar;
                if (!target.RecordTrain) { Report(rank, "未录入：本模块未开启录制，请点击播放按钮"); return; }
                if (!enabled) { Report(rank, "未录入：本模块的 Sonar 聊天录制设置已关闭"); return; }
            }
            else if (!(_a.RecordTrain || _s.RecordTrain)) return;
            if (links.Count != 1) { Report(rank, "未录入：需要一个有效游戏地图链接，普通文本坐标无法录制"); return; }
            var link = links[0];
            var map = _data.GetExcelSheet<Map>().GetRowOrDefault(link.Map.RowId);
            if (map == null || map.Value.TerritoryType.RowId != link.TerritoryType.RowId)
            { Report(rank, "未录入：地图链接与地区不匹配"); return; }
            var relayMap = new HuntRelayMap(link.TerritoryType.RowId, link.Map.RowId, new Vector2(link.XCoord, link.YCoord),
                MapHelpers.MapScaleToMaxCoord(map.Value.SizeFactor) + 1, MapHelpers.GetMapName(link.TerritoryType.RowId));
            var worlds = _worlds.Get(_a.CurrentWorldId, _a.GetImportWorlds().Concat(_s.GetImportWorlds()));
            if (!HuntRelayParser.TryParse(before, after, relayMap, _hunts.RelaySpecies, worlds, _glyphs,
                DateTime.UtcNow, out var relay, out var failure, tagged) || relay == null)
            { Report(rank, "未录入：" + failure); return; }
            rank = relay.Rank;
            var manager = rank == "A" ? _a : _s;
            var capture = rank == "A" ? _config.HuntTrainCaptureSonar : _config.SRankTrain.HuntTrainCaptureSonar;
            if (!manager.RecordTrain || !capture) { Report(rank, "未录入：请开启本模块录制及 Sonar 聊天录制设置"); return; }
            var outcome = manager.RecordRelay(relay.Mob);
            if (outcome == RelayRecordResult.Ignored) { Report(rank, "未录入：记录区服或目标无效"); return; }
            var action = outcome == RelayRecordResult.Added ? "已录入" : "已更新";
            Report(rank, $"{action} {relay.Mob.Name}（{relay.Mob.WorldName}，{(relay.Mob.Instance == 0 ? "未指定分线" : $"{relay.Mob.Instance}线")}）");
        }
        catch (Exception ex)
        {
            Report(rank, "未录入：解析失败，请查看卫月日志");
            PluginLog.Warning(ex, "Could not record structured Sonar chat message");
        }
    }

    private void Report(string? rank, string message)
    {
        var text = $"Sonar：{message} · {DateTime.Now:HH:mm:ss}";
        if (rank == "A" || rank == null && _a.RecordTrain) AStatus = text;
        if (rank == "S" || rank == null && _s.RecordTrain) SStatus = text;
    }

    public void Dispose()=>_chat.ChatMessage-=OnChat;
}
