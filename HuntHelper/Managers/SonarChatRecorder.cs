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
        if (!_client.IsLoggedIn || !(_a.RecordTrain && _config.HuntTrainCaptureSonar ||
            _s.RecordTrain && _config.SRankTrain.HuntTrainCaptureSonar)) return;
        try
        {
            var payloads=message.Message.Payloads;
            var links=payloads.OfType<MapLinkPayload>().ToList();
            if(links.Count!=1)return;
            var link=links[0];var index=payloads.IndexOf(link);
            var before=string.Concat(payloads.Take(index).OfType<TextPayload>().Select(p=>p.Text));
            var after=string.Concat(payloads.Skip(index+1).OfType<TextPayload>().Select(p=>p.Text));
            var tagged=payloads.OfType<DalamudLinkPayload>().Any(p=>p.Plugin.Contains("Sonar",StringComparison.OrdinalIgnoreCase)) ||
                before.Contains("[Sonar]",StringComparison.OrdinalIgnoreCase);
            var map=_data.GetExcelSheet<Map>().GetRowOrDefault(link.Map.RowId);
            if(map==null || map.Value.TerritoryType.RowId!=link.TerritoryType.RowId)return;
            var relayMap=new HuntRelayMap(link.TerritoryType.RowId,link.Map.RowId,new Vector2(link.XCoord,link.YCoord),
                MapHelpers.MapScaleToMaxCoord(map.Value.SizeFactor)+1, MapHelpers.GetMapName(link.TerritoryType.RowId));
            var worlds = _worlds.Get(_a.CurrentWorldId, _a.GetImportWorlds().Concat(_s.GetImportWorlds()));
            if(!HuntRelayParser.TryParse(before,after,relayMap,_hunts.RelaySpecies,worlds,_glyphs,
                DateTime.UtcNow,out var relay,tagged) || relay==null)return;
            var target=relay.Rank=="A"?_a:_s;
            var enabled=relay.Rank=="A"?_config.HuntTrainCaptureSonar:_config.SRankTrain.HuntTrainCaptureSonar;
            if(!enabled || !target.RecordTrain)return;
            var outcome=target.RecordRelay(relay.Mob);
            if(outcome==RelayRecordResult.Ignored)return;
            var action=outcome==RelayRecordResult.Added?"已录入":"已更新";
            var status=$"Sonar：{action} {relay.Mob.Name}（{relay.Mob.WorldName}，{(relay.Mob.Instance==0?"无分线":$"{relay.Mob.Instance}线")}）";
            if(relay.Rank=="A")AStatus=status;else SStatus=status;
        }
        catch(Exception ex){PluginLog.Warning(ex,"Could not record structured Sonar chat message");}
    }

    public void Dispose()=>_chat.ChatMessage-=OnChat;
}
