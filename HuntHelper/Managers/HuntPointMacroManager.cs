using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Managers;

public sealed class HuntPointMacroManager : IDisposable
{
    private readonly Configuration _config;
    private readonly IPlayerState _player;
    private readonly IClientState _client;
    private readonly IFramework _framework;
    private readonly Queue<(HuntPointChatLine Line, HuntPointChatFlag Flag)> _queue = new();
    private readonly IHuntPointChatHost _chatHost;
    private readonly Func<long> _now;
    private long _nextSend;
    private long? _waitUntil;
    private string _completionStatus = "宏等待已完成（未发送聊天）";
    private uint _senderWorld;
    public string Status { get; private set; } = string.Empty;
    public bool Busy => _queue.Count != 0 || _waitUntil.HasValue;

    public HuntPointMacroManager(Configuration config, IPlayerState player, IClientState client, IFramework framework,
        IHuntPointChatHost? chatHost = null, Func<long>? now = null)
    {
        _config = config; _player = player; _client = client; _framework = framework;
        _chatHost = chatHost ?? new GameHuntPointChatHost(); _now = now ?? (() => Environment.TickCount64);
        _framework.Update += Tick;
    }

    public void Send(HuntTrainMob mob, string rank)
        => SendTemplate(rank == "S" ? _config.PointMacros.S : _config.PointMacros.A, mob, rank);

    public void SendTemplate(string template, HuntTrainMob? mob, string rank)
    {
        if (Busy) { Status = "坐标宏正在发送，请稍候或取消"; return; }
        try
        {
            if (!_client.IsLoggedIn || !_player.IsLoaded) throw new InvalidOperationException("请先登录并等待角色加载");
            var lines = HuntPointMacro.Expand(template, mob, rank, _player.CurrentWorld.RowId);
            // Build and validate the complete snapshot before enqueueing any line.
            var pending = HuntPointChatDelivery.Build(lines);
            HuntPointChatFlag flag = default;
            if (pending.Any(line => line.UsesFlag))
            {
                var target = mob ?? throw new InvalidOperationException("请先选中怪物记录");
                var link = new MapLinkPayload(target.TerritoryID, target.MapID, target.Position.X, target.Position.Y);
                flag = new(target.TerritoryID, target.MapID, link.RawX / 1000f, link.RawY / 1000f);
            }
            foreach (var line in pending) _queue.Enqueue((line, flag));
            _senderWorld = _player.CurrentWorld.RowId;
            _nextSend = 0;
            _waitUntil = null;
            _completionStatus = "宏等待已完成（未发送聊天）";
            Status = mob == null ? "已准备发送聊天宏" : $"已准备发送 {rank} 坐标宏：{mob.WorldName} · {mob.Name}";
        }
        catch (Exception ex) { Status = "坐标宏未发送：" + ex.Message; }
    }

    public void Cancel() { _queue.Clear(); _waitUntil = null; Status = "已取消剩余坐标宏"; }

    private void Tick(IFramework _)
    {
        if (!Busy) return;
        if (!_client.IsLoggedIn || !_player.IsLoaded || _player.CurrentWorld.RowId != _senderWorld) { Cancel(); return; }
        try
        {
            var now = _now();
            if (_waitUntil is { } until)
            {
                if (now < until)
                {
                    Status = $"坐标宏等待中，剩余 {(until - now + 999) / 1000} 秒 · 后续 {_queue.Count} 行";
                    return;
                }
                _waitUntil = null;
                if (_queue.Count == 0) { Status = _completionStatus; return; }
            }
            while (_queue.Count > 0)
            {
                var next = _queue.Peek();
                if (!next.Line.IsWaitOnly)
                {
                    if (now < _nextSend) return;
                    HuntPointChatDelivery.Send(next.Line, next.Flag, _chatHost);
                    _nextSend = _now() + 1200;
                    _completionStatus = next.Line.LocalOnly
                        ? "本地回显已执行（/echo，仅自己可见）"
                        : $"已提交 {next.Line.Channel} 频道；是否送达以游戏反馈为准";
                }
                _queue.Dequeue();
                if (next.Line.WaitSeconds > 0)
                {
                    _waitUntil = _now() + next.Line.WaitSeconds * 1000L;
                    Status = $"坐标宏等待中，剩余 {next.Line.WaitSeconds} 秒 · 后续 {_queue.Count} 行";
                    return;
                }
                Status = Busy ? $"坐标宏发送中，剩余 {_queue.Count} 行" : _completionStatus;
                // A pure wait starts immediately after this line, without adding a chat interval.
                now = _now();
            }
        }
        catch (Exception ex) { _queue.Clear(); _waitUntil = null; Status = "坐标宏发送失败：" + ex.Message; }
    }

    public void Dispose() { _framework.Update -= Tick; _queue.Clear(); _waitUntil = null; }
}
