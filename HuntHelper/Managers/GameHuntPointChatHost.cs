using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using HuntHelper.Utilities;
using System;

namespace HuntHelper.Managers;

/// <summary>Only the native game boundary. Flag generation and outgoing chat use normal client commands.</summary>
public sealed unsafe class GameHuntPointChatHost : IHuntPointChatHost
{
    public IDisposable UseFlag(HuntPointChatFlag target)
    {
        if (target.TerritoryId == 0 || target.MapId == 0 || !float.IsFinite(target.X) || !float.IsFinite(target.Z))
            throw new InvalidOperationException("怪物记录的地图旗标无效");
        var map = AgentMap.Instance();
        if (map == null) throw new InvalidOperationException("游戏地图旗标尚未就绪");
        var restore = new RestoreFlag(map, map->FlagMarkerCount, map->FlagMapMarkers[0]);
        try
        {
            map->FlagMarkerCount = 0;
            map->SetFlagMapMarker(target.TerritoryId, target.MapId, target.X, target.Z);
            if (map->FlagMarkerCount != 1 || map->FlagMapMarkers[0].TerritoryId != target.TerritoryId ||
                map->FlagMapMarkers[0].MapId != target.MapId)
                throw new InvalidOperationException("未能设置该怪物的旗标，消息未发送");
            return restore;
        }
        catch { restore.Dispose(); throw; }
    }

    public void SendText(string message)
    {
        var module = UIModule.Instance();
        if (module == null) throw new InvalidOperationException("游戏聊天尚未就绪");
        // FromString constructs a terminated UTF-8 input string, not a pre-rendered chat link.
        var text = Utf8String.FromString(message);
        try
        {
            text->SanitizeString((AllowedEntities)0x27F);
            if (text->ToString() != message) throw new InvalidOperationException("宏含游戏不接受的字符，消息未发送");
            module->ProcessChatBoxEntry(text, IntPtr.Zero, false);
        }
        finally { text->Dtor(true); }
    }

    private sealed class RestoreFlag(AgentMap* map, byte count, FlagMapMarker marker) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            map->FlagMapMarkers[0] = marker;
            map->FlagMarkerCount = count;
        }
    }
}
