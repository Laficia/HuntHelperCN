using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using HuntHelper.Utilities;
using Newtonsoft.Json;
using System;
using System.Numerics;

namespace HuntHelper.Managers.Hunts.Models;

public class HuntTrainMob
{
    public string Name { get; set; }
    public uint MobID { get; init; }
    public string MapName { get; init; } //map name never actually gets used...
    public DateTime LastSeenUTC { get; set; }
    public Vector2 Position { get; set; }
    [JsonIgnore]
    public SeString MapLink => SeString.CreateMapLink(TerritoryID, MapID, Position.X, Position.Y)!;
    public bool Dead { get; set; }

    public uint TerritoryID { get; init; }
    public uint MapID { get; init; }
    public uint Instance { get; init; }
    public uint WorldID { get; init; }
    public string WorldName { get; init; }
    public string DataCenterName { get; init; }
    public Vector3? WorldPosition { get; set; }

    // An explicitly exported world 0 must remain unknown; only old sharing codes
    // without a WorldID field should inherit their import destination.
    [JsonIgnore]
    public bool HasWorldMetadata { get; }

    [JsonConstructor]
    public HuntTrainMob(string name, uint mobId, uint territoryId, uint mapId, uint instance, string mapName, Vector2 position, DateTime lastSeenUTC, bool dead = false,
        uint? worldId = null, string worldName = "", string dataCenterName = "", Vector3? worldPosition = null)
    {
        Name = name;
        MobID = mobId;
        MapName = mapName;
        Position = position;
        LastSeenUTC = lastSeenUTC;
        Dead = dead;
        TerritoryID = territoryId;
        MapID = mapId;
        Instance = instance;
        WorldID = worldId ?? 0;
        WorldName = HuntTrainWorld.GetDisplayName(WorldID, worldName);
        DataCenterName = dataCenterName ?? string.Empty;
        WorldPosition = worldPosition;
        HasWorldMetadata = worldId.HasValue;
    }


    /// <summary>
    /// returns the time elapsed, in minutes, since the mob was last seen.
    /// </summary>
    /// <returns></returns>
    public int TimeSinceLastSeen()
    {
        return 1;
    }

    /// <summary>
    /// marks the mob as dead
    /// </summary>
    public void MarkDead()
    {
        Dead = true;
    }

    public bool IsSameAs(uint mobID, uint instance)
    {
        return MobID == mobID && Instance == instance;
    }

    public bool IsSameAs(uint mobID, uint instance, uint worldID) =>
        IsSameAs(mobID, instance) && WorldID == worldID;

    public HuntTrainMob WithWorld(HuntTrainWorld world) =>
        new(Name, MobID, TerritoryID, MapID, Instance, MapName, Position, LastSeenUTC,
            Dead, world.WorldID, world.WorldName, world.DataCenterName, WorldPosition);
}

public static class HuntTrainMobExtensions
{
    public static HuntTrainMob ToHuntTrainMob(
        this IBattleNpc mob,
        uint territoryId,
        uint mapId,
        uint instance,
        string mapName,
        float zoneMapCoordSize,
        bool isDead = false,
        uint? worldId = null,
        string worldName = "",
        string dataCenterName = ""
    ) =>
        new(
            mob.Name.TextValue,
            mob.NameId,
            territoryId,
            mapId,
            instance,
            mapName,
            MapHelpers.ConvertToMapCoordinate(mob.Position, zoneMapCoordSize),
            DateTime.UtcNow,
            isDead,
            worldId,
            worldName,
            dataCenterName,
            mob.Position
        );
}
