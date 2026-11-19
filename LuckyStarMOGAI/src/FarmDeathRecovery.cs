using System;

namespace LuckyStar;

internal enum RecoveryAction { Waiting, Resume, Failed }

internal sealed class FarmDeathRecovery(FarmScope destination, long startedAt)
{
    private bool returnRequested;
    private uint teleportFrom;
    private bool sawTeleportTransition;
    private long? readySince;
    public string State { get; private set; } = "等待返回复活点";

    public static bool CanBegin(FarmContext context, FarmScope destination) =>
        context.Dead && context.Ready && context.LoggedIn && !context.Loading &&
        destination.World != 0 && destination.Territory != 0 && context.Scope == destination;

    public RecoveryAction Tick(FarmContext context, long now, bool inFarmArea,
        Func<bool> requestReturn, Func<string?> requestTeleport, Func<bool> navigationReady)
    {
        if (now - startedAt >= 120_000) return Fail("死亡恢复超时，农怪已停止");
        if (!context.LoggedIn && !context.Loading) return Fail("已登出，停止死亡恢复");
        if (context.Scope.World != 0 && context.Scope.World != destination.World)
            return Fail("已离开原服务器，停止死亡恢复");

        if (context.Loading || !context.Ready)
        {
            if (teleportFrom != 0 && context.Loading) sawTeleportTransition = true;
            readySince = null;
            return RecoveryAction.Waiting;
        }
        if (context.Scope.World == 0 || context.Scope.Territory == 0) return RecoveryAction.Waiting;

        if (context.Dead)
        {
            readySince = null;
            if (context.Scope != destination || teleportFrom != 0) return Fail("恢复途中再次死亡或位置已变化，农怪已停止");
            if (!returnRequested && requestReturn())
            {
                returnRequested = true;
                State = "等待复活点加载";
            }
            return RecoveryAction.Waiting;
        }

        if (teleportFrom != 0)
        {
            if (context.Scope.Territory != teleportFrom) sawTeleportTransition = true;
            if (!sawTeleportTransition) return RecoveryAction.Waiting;
            if (context.Scope.Territory != destination.Territory)
            {
                if (context.Scope.Territory != teleportFrom) return Fail("传送未到达原农怪地图，农怪已停止");
                return RecoveryAction.Waiting;
            }
        }

        if (context.Busy || context.Casting || context.Combat)
        {
            readySince = null;
            return RecoveryAction.Waiting;
        }

        if (context.Scope.Territory == destination.Territory && inFarmArea)
        {
            // Use the same settled-state check for a same-map revival or a teleport arrival.
            State = "等待原地图和导航就绪";
            readySince ??= now;
            if (now - readySince.Value < 750) return RecoveryAction.Waiting;
            if (context.Scope.Instance != destination.Instance)
                return Fail("返回了不同分线，农怪已停止");
            return navigationReady() ? RecoveryAction.Resume : RecoveryAction.Waiting;
        }

        readySince = null;
        if (teleportFrom != 0) return RecoveryAction.Waiting;

        var error = requestTeleport();
        if (error != null) return Fail(error);
        teleportFrom = context.Scope.Territory;
        State = "正在传送回原农怪地图";
        return RecoveryAction.Waiting;
    }

    private RecoveryAction Fail(string reason)
    {
        State = reason;
        return RecoveryAction.Failed;
    }
}
