using System;
using HuntHelper.Managers.Hunts.Models;

namespace HuntHelper.Managers.Automation;

internal sealed class HuntCrystalTravel(DRWorldTravel worldTravel, Func<uint, bool> teleport)
{
    private sealed record Request(HuntTrainWorld World, uint Crystal, long Deadline);
    private Request? pending;

    public bool Busy => pending != null;
    public string Status { get; private set; } = string.Empty;

    public bool Begin(HuntTrainWorld world, uint crystal, long now)
    {
        if (Busy) return false;
        if (!worldTravel.Change(world, out var error))
        {
            Status = error;
            return false;
        }

        pending = new(world, crystal, now + 600_000);
        Status = $"正在前往「{world.WorldName}」，到服后传送水晶";
        return true;
    }

    public bool Send(uint crystal, string worldName)
    {
        var sent = teleport(crystal);
        Status = sent ? $"已发起「{worldName}」的水晶传送" : "水晶传送未能发起，请检查水晶解锁与角色状态";
        return sent;
    }

    public void Tick(uint currentWorld, bool ready, bool canTeleport, long now)
    {
        if (pending is not { } request) return;
        if (now >= request.Deadline)
        {
            Cancel($"等待「{request.World.WorldName}」超时，未发起水晶传送。{DRWorldTravel.StopHint}");
            return;
        }
        // Poll through loading screens and intermediate worlds so DR resets its arrival timer.
        var worldTravelBusy = worldTravel.Busy;
        if (worldTravelBusy || !ready || currentWorld != request.World.WorldID || !canTeleport) return;

        pending = null;
        Send(request.Crystal, request.World.WorldName);
    }

    public void Cancel(string status)
    {
        if (!Busy) return;
        pending = null;
        worldTravel.ReleaseOwnedRequest();
        Status = status;
    }
}
