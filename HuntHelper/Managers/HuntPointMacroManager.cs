using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;

namespace HuntHelper.Managers;

public sealed class HuntPointMacroManager : IDisposable
{
    private readonly Configuration _config;
    private readonly IPlayerState _player;
    private readonly IClientState _client;
    private readonly IFramework _framework;
    private readonly Queue<byte[]> _queue = new();
    private long _nextSend;
    private uint _senderWorld;
    public string Status { get; private set; } = string.Empty;
    public bool Busy => _queue.Count != 0;

    public HuntPointMacroManager(Configuration config, IPlayerState player, IClientState client, IFramework framework)
    { _config = config; _player = player; _client = client; _framework = framework; _framework.Update += Tick; }

    public void Send(HuntTrainMob mob, string rank)
    {
        if (Busy) { Status = "坐标宏正在发送，请稍候或取消"; return; }
        try
        {
            if (!_client.IsLoggedIn || !_player.IsLoaded) throw new InvalidOperationException("请先登录并等待角色加载");
            var template = rank == "S" ? _config.PointMacros.S : _config.PointMacros.A;
            var lines = HuntPointMacro.Expand(template, mob, rank, _player.CurrentWorld.RowId);
            // Build and validate the complete snapshot before enqueueing any line.
            var pending = new List<byte[]>();
            foreach (var line in lines)
            {
                var builder = new SeStringBuilder();
                foreach (var part in line)
                    if (part.IsFlag) builder.Append(mob.MapLink); else builder.AddText(part.Text);
                var bytes = builder.BuiltString.Encode();
                if (bytes.Length > 500) throw new InvalidOperationException("展开后的单行宏超过 500 字节，请缩短内容");
                pending.Add(bytes);
            }
            foreach (var bytes in pending) _queue.Enqueue(bytes);
            _senderWorld = _player.CurrentWorld.RowId;
            _nextSend = 0;
            Status = $"已准备发送 {rank} 坐标宏：{mob.WorldName} · {mob.Name}";
        }
        catch (Exception ex) { Status = "坐标宏未发送：" + ex.Message; }
    }

    public void Cancel() { _queue.Clear(); Status = "已取消剩余坐标宏"; }

    private unsafe void Tick(IFramework _)
    {
        if (!Busy) return;
        if (!_client.IsLoggedIn || !_player.IsLoaded || _player.CurrentWorld.RowId != _senderWorld) { Cancel(); return; }
        if (Environment.TickCount64 < _nextSend) return;
        try
        {
            var module = UIModule.Instance();
            if (module == null) throw new InvalidOperationException("游戏聊天尚未就绪");
            var text = Utf8String.FromSequence(_queue.Dequeue());
            try { module->ProcessChatBoxEntry(text, IntPtr.Zero, false); }
            finally { text->Dtor(true); }
            _nextSend = Environment.TickCount64 + 1200;
            Status = Busy ? $"坐标宏发送中，剩余 {_queue.Count} 行" : "坐标宏已交给游戏聊天发送";
        }
        catch (Exception ex) { _queue.Clear(); Status = "坐标宏发送失败：" + ex.Message; }
    }

    public void Dispose() { _framework.Update -= Tick; _queue.Clear(); }
}
