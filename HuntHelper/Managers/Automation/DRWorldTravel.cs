using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using HuntHelper.Managers.Hunts.Models;

namespace HuntHelper.Managers.Automation;

/// <summary>Uses DR's public module query and FastWorldTravel command, never Lifestream.</summary>
public sealed class DRWorldTravel
{
    private readonly ICallGateSubscriber<string, bool?> enabled;
    private readonly Func<string, bool> command;
    private readonly Func<(uint World, bool Ready)> context;
    private readonly Func<bool> gameBusy;
    private readonly Func<string> currentDataCenter;
    private readonly ICallGateSubscriber<bool> dcTravelerValid;
    private readonly Func<long> clock;
    private uint target;
    private long arrivedAt;
    public const string StopHint = "已提交的跨服任务请在 DR 中停止";

    public DRWorldTravel(IDalamudPluginInterface plugin, Func<string, bool> command,
        Func<(uint World, bool Ready)> context, Func<bool> gameBusy, Func<string> currentDataCenter, Func<long>? clock = null)
    {
        enabled = plugin.GetIpcSubscriber<string, bool?>("DailyRoutines.IsModuleEnabled");
        dcTravelerValid = plugin.GetIpcSubscriber<bool>("DCTravelerX.IsValid");
        this.command = command; this.context = context; this.gameBusy = gameBusy;
        this.currentDataCenter = currentDataCenter;
        this.clock = clock ?? (() => Environment.TickCount64);
    }

    public string Error
    {
        get
        {
            try
            {
                return enabled.HasFunction && enabled.InvokeFunc("FastWorldTravel") == true ? "" :
                    "请安装 Daily Routines 并启用「快捷跨服」模块；国服跨大区还需 DCTravelerX";
            }
            catch { return "DR 跨服接口不可用，请确认 Daily Routines 已加载并启用「快捷跨服」"; }
        }
    }

    // DR has no public FastWorldTravel busy/cancel IPC. A dispatched command is
    // not arrival: keep our own wait through city teleports, logout and DC travel.
    public bool Busy
    {
        get
        {
            if (target == 0) return gameBusy();
            var current = context();
            if (!current.Ready || current.World != target || gameBusy())
            { arrivedAt = 0; return true; }
            if (arrivedAt == 0) arrivedAt = clock();
            if (clock() - arrivedAt < 2000) return true;
            target = 0; arrivedAt = 0;
            return false;
        }
    }

    public string VisitProblem(HuntTrainWorld world)
    {
        if (world.WorldID == 0 || string.IsNullOrWhiteSpace(world.WorldName)) return "请选择有效服务器";
        var error = Error;
        if (error.Length != 0) return error;
        var current = currentDataCenter();
        if (current.Length == 0 || world.DataCenterName.Length == 0 || current == world.DataCenterName) return "";
        try { if (dcTravelerValid.HasFunction && dcTravelerValid.InvokeFunc()) return ""; }
        catch { }
        return "DR 国服跨大区需要安装并启用 DCTravelerX，且其服务可用";
    }

    public bool Change(HuntTrainWorld world, out string error)
    {
        error = VisitProblem(world);
        if (error.Length != 0) return false;
        if (Busy) { error = "正在跨服，请等待当前任务完成"; return false; }
        try
        {
            // The module explicitly accepts numeric world IDs, avoiding fuzzy name matches.
            if (!command("/pdr worldtravel " + world.WorldID))
            { error = "DR 跨服命令未被接收，请确认「快捷跨服」已启用"; return false; }
            target = world.WorldID; arrivedAt = 0;
            return true;
        }
        catch (Exception ex) { error = "DR 跨服请求失败：" + ex.Message; return false; }
    }

    // Do not unload DR modules or guess a nonexistent abort command.
    public void ReleaseOwnedRequest() { target = 0; arrivedAt = 0; }
}
