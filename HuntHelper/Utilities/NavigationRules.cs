using System;
using System.Numerics;

namespace HuntHelper.Utilities;

public static class NavigationRules
{
    public static string GetLocationMismatch(uint worldId, uint territoryId, uint mapId, uint instance,
        uint currentWorldId, uint currentTerritoryId, uint currentMapId, uint currentInstance)
    {
        if (worldId == 0) return "记录尚未标注区服，请先重新扫描或导入带区服的记录";
        if (worldId != currentWorldId) return "只能移动到当前区服的 怪物";
        if (territoryId != currentTerritoryId || mapId != currentMapId) return "只有在同一张地图时才能移动";
        if (instance != currentInstance) return "请先切换到该 怪物所在分线";
        return string.Empty;
    }

    // Inverts HuntHelper's stored map-coordinate formula. The second map coordinate is world Z.
    public static Vector3 FromHuntMapCoordinates(Vector2 position, float sizeFactor)
    {
        if (sizeFactor <= 0 || !float.IsFinite(sizeFactor) || !float.IsFinite(position.X) || !float.IsFinite(position.Y))
            throw new ArgumentOutOfRangeException(nameof(position), "地图坐标无效");
        return new Vector3((position.X - 2048f / sizeFactor - 1f) * 50f, 1024f,
            (position.Y - 2048f / sizeFactor - 1f) * 50f);
    }

    public static bool IsFinite(Vector3 position) =>
        float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z);
}
