using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System;

namespace HuntHelper.Managers.Automation;

/// <summary>Matches the API-15 CN Lifestream 2.5.4.15 IPC signatures, also used upstream.</summary>
public sealed class LifestreamWorldTravel
{
    private readonly ICallGateSubscriber<bool> _busy;
    private readonly ICallGateSubscriber<uint, bool> _changeId;
    private readonly ICallGateSubscriber<string, bool> _change, _sameDc, _crossDc;
    private readonly ICallGateSubscriber<object> _abort;
    public LifestreamWorldTravel(IDalamudPluginInterface plugin)
    {
        _busy = plugin.GetIpcSubscriber<bool>("Lifestream.IsBusy");
        _changeId = plugin.GetIpcSubscriber<uint, bool>("Lifestream.ChangeWorldById");
        _change = plugin.GetIpcSubscriber<string, bool>("Lifestream.ChangeWorld");
        _sameDc = plugin.GetIpcSubscriber<string, bool>("Lifestream.CanVisitSameDC");
        _crossDc = plugin.GetIpcSubscriber<string, bool>("Lifestream.CanVisitCrossDC");
        _abort = plugin.GetIpcSubscriber<object>("Lifestream.Abort");
    }
    public string Error => !_busy.HasFunction || !_abort.HasAction || (!_changeId.HasFunction && !_change.HasFunction) || !_sameDc.HasFunction || !_crossDc.HasFunction
        ? "多服务器找怪需要下载并启用国服 Lifestream（API 15，含跨服 IPC）" : string.Empty;
    public bool Busy
    {
        get { try { return _busy.InvokeFunc(); } catch { return true; } }
    }
    public string VisitProblem(HuntTrainWorld world)
    {
        if (Error.Length != 0) return Error;
        try { return _sameDc.InvokeFunc(world.WorldName) || _crossDc.InvokeFunc(world.WorldName) ? string.Empty : $"Lifestream 当前无法访问「{world.WorldName}」，请检查跨服限制"; }
        catch (Exception ex) { return "Lifestream 接口不可用：" + ex.Message; }
    }
    public bool Change(HuntTrainWorld world, out string error)
    {
        error = VisitProblem(world); if (error.Length != 0) return false;
        try
        {
            if (Busy) { error = "Lifestream 正在执行其他任务，请先等待其完成"; return false; }
            var accepted = _changeId.HasFunction ? _changeId.InvokeFunc(world.WorldID) : _change.InvokeFunc(world.WorldName);
            if (!accepted) error = $"Lifestream 未接受前往「{world.WorldName}」的请求";
            return accepted;
        }
        catch (Exception ex) { error = "Lifestream 跨服失败：" + ex.Message; return false; }
    }
    public void AbortOwnedRequest()
    {
        try { if (_busy.HasFunction && _busy.InvokeFunc() && _abort.HasAction) _abort.InvokeAction(); }
        catch (Exception ex) { PluginLog.Warning(ex, "Unable to stop owned Lifestream travel"); }
    }
}
