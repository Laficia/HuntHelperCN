using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HuntHelper.Managers.Hunts;

public class TrainList<T> : List<T>
{
    public event Action? TrainChanged;

    public new void Add(T item)
    {
        base.Add(item);
        NotifyChanged();
    }

    public new void AddRange(IEnumerable<T> collection)
    {
        base.AddRange(collection);
        NotifyChanged();
    }

    public new bool Remove(T item)
    {
        var removed = base.Remove(item);
        if (removed) NotifyChanged();
        return removed;
    }

    public new int RemoveAll(Predicate<T> match)
    {
        var removed = base.RemoveAll(match);
        if (removed > 0) NotifyChanged();
        return removed;
    }

    public new void Clear()
    {
        if (Count == 0) return;
        base.Clear();
        NotifyChanged();
    }

    public void ReplaceWith(IEnumerable<T> collection)
    {
        var replacement = collection.ToList();
        base.Clear();
        base.AddRange(replacement);
        NotifyChanged();
    }

    public void Swap(int first, int second)
    {
        (base[first], base[second]) = (base[second], base[first]);
        NotifyChanged();
    }

    public void NotifyChanged() => TrainChanged?.Invoke();
}

public class TrainManager : IDisposable
{
    private readonly IChatGui _chatGui;
    private readonly IGameGui _gameGui;
    private readonly Func<HuntTrainWorld> _currentWorldProvider;
    private readonly string _huntTrainFilePath;
    private readonly Dictionary<uint, TrainList<HuntTrainMob>> _trainsByWorld = new();
    private readonly Dictionary<uint, HuntTrainWorld> _worlds = new();
    private readonly object _saveLock = new();
    private uint _selectedWorldId;
    private bool _selectionChosen;
    private bool _suppressSave;
    private bool _canSave = true;
    private DateTime _lastSaveUtc;

    public TrainList<HuntTrainMob> HuntTrain => GetOrCreateTrain(SelectedWorldId);
    public readonly List<HuntTrainMob> ImportedTrain = new();
    public bool RecordTrain = false;
    public bool ImportFromIPC = false;
    public string? PersistenceError { get; private set; }

    public uint SelectedWorldId
    {
        get
        {
            if (!_selectionChosen) GetCurrentWorld();
            return _selectedWorldId;
        }
    }

    public uint CurrentWorldId => GetCurrentWorld().WorldID;

    public TrainManager(IChatGui chatGui, IGameGui gameGui, string huntTrainFilePath, IPlayerState playerState)
        : this(chatGui, gameGui, huntTrainFilePath, () => ReadCurrentWorld(playerState))
    {
    }

    public TrainManager(IChatGui chatGui, IGameGui gameGui, string huntTrainFilePath, Func<HuntTrainWorld> currentWorldProvider)
    {
        _chatGui = chatGui;
        _gameGui = gameGui;
        _huntTrainFilePath = huntTrainFilePath;
        _currentWorldProvider = currentWorldProvider;
        LoadHuntTrainRecord();
        GetCurrentWorld();
    }

    private static HuntTrainWorld ReadCurrentWorld(IPlayerState playerState)
    {
        var world = playerState.CurrentWorld;
        return new HuntTrainWorld(world.RowId,
            world.ValueNullable?.Name.ToString() ?? string.Empty,
            world.ValueNullable?.DataCenter.ValueNullable?.Name.ToString() ?? string.Empty, 0);
    }

    public HuntTrainWorld GetCurrentWorld()
    {
        var world = _currentWorldProvider();
        RememberWorld(world);
        GetOrCreateTrain(world.WorldID);
        // Follow the visited world until the user explicitly chooses a tab.
        if (!_selectionChosen && world.WorldID != 0) _selectedWorldId = world.WorldID;
        return WorldWithCount(world.WorldID);
    }

    public IReadOnlyList<HuntTrainWorld> GetWorlds()
    {
        GetCurrentWorld();
        return _worlds.Keys
            .Where(id => _trainsByWorld.TryGetValue(id, out var train) && train.Count > 0)
            .Select(WorldWithCount)
            .OrderBy(w => w.WorldID == 0)
            .ThenBy(w => w.DataCenterName)
            .ThenBy(w => w.WorldName)
            .ToList();
    }

    public void SelectWorld(uint worldID)
    {
        _selectionChosen = true;
        _selectedWorldId = worldID;
        GetOrCreateTrain(worldID);
    }

    // Include known empty worlds for import choices without creating empty tabs.
    public IReadOnlyList<HuntTrainWorld> GetImportWorlds()
    {
        GetCurrentWorld();
        return _worlds.Keys.Select(WorldWithCount).ToList();
    }

    public bool CanChooseImportWorld => ImportedTrain.Count > 0 &&
        ImportedTrain.All(m => m.WorldID == ImportedTrain[0].WorldID);

    public bool SetImportWorld(HuntTrainWorld world)
    {
        if (!CanChooseImportWorld) return false;
        // Change only the preview. Existing trains and the clipboard stay intact until confirmation.
        var replacement = ImportedTrain.Select(m => m.WithWorld(world)).ToList();
        RememberWorld(world);
        ImportedTrain.Clear();
        ImportedTrain.AddRange(replacement);
        return true;
    }

    private void RememberWorld(HuntTrainWorld world)
    {
        var name = HuntTrainWorld.GetDisplayName(world.WorldID, world.WorldName);
        var dataCenter = world.DataCenterName ?? string.Empty;
        if (_worlds.TryGetValue(world.WorldID, out var previous))
        {
            if (name == HuntTrainWorld.GetDisplayName(world.WorldID, string.Empty)) name = previous.WorldName;
            if (string.IsNullOrWhiteSpace(dataCenter)) dataCenter = previous.DataCenterName;
        }
        _worlds[world.WorldID] = new HuntTrainWorld(world.WorldID, name, dataCenter, 0);
    }

    private HuntTrainWorld WorldWithCount(uint worldID)
    {
        var world = _worlds[worldID];
        return world with { Count = _trainsByWorld.TryGetValue(worldID, out var train) ? train.Count : 0 };
    }

    private TrainList<HuntTrainMob> GetOrCreateTrain(uint worldID)
    {
        if (_trainsByWorld.TryGetValue(worldID, out var train)) return train;
        train = new TrainList<HuntTrainMob>();
        train.TrainChanged += OnTrainChanged;
        _trainsByWorld.Add(worldID, train);
        if (!_worlds.ContainsKey(worldID))
            RememberWorld(new HuntTrainWorld(worldID, string.Empty, string.Empty, 0));
        return train;
    }

    private void OnTrainChanged()
    {
        if (!_suppressSave) SaveHuntTrainRecord();
    }

    public void Dispose()
    {
        SaveHuntTrainRecord();
        foreach (var train in _trainsByWorld.Values) train.TrainChanged -= OnTrainChanged;
    }

    public void AddMob(IBattleNpc mob, uint territoryid, uint mapid, uint instance, string mapName, float zoneMapCoordSize) =>
        AddMob(mob.ToHuntTrainMob(territoryid, mapid, instance, mapName, zoneMapCoordSize));

    public void ObserveMob(IBattleNpc mob, bool isARank, uint territoryId, uint mapId, uint instance,
        string mapName, float zoneMapCoordSize)
    {
        if (UpdateLastSeen(mob, instance, zoneMapCoordSize)) return;
        if (RecordTrain && isARank) AddMob(mob, territoryId, mapId, instance, mapName, zoneMapCoordSize);
    }

    // Recording always targets the character's visited world, independently of the UI tab.
    public void AddMob(HuntTrainMob mob)
    {
        var world = GetCurrentWorld();
        var train = GetOrCreateTrain(world.WorldID);
        if (train.Any(m => m.IsSameAs(mob.MobID, mob.Instance, world.WorldID))) return;
        train.Add(mob.WithWorld(world));
    }

    public bool UpdateLastSeen(IBattleNpc mob, uint instance, float? zoneMapCoordSize = null)
    {
        var world = GetCurrentWorld();
        var existing = GetOrCreateTrain(world.WorldID).FirstOrDefault(m => m.IsSameAs(mob.NameId, instance, world.WorldID));
        if (existing == null) return false;
        existing.LastSeenUTC = DateTime.UtcNow;
        existing.WorldPosition = mob.Position;
        if (zoneMapCoordSize.HasValue)
            existing.Position = MapHelpers.ConvertToMapCoordinate(mob.Position, zoneMapCoordSize.Value);
        // Scans run every frame; persist updates periodically instead of writing every frame.
        if (DateTime.UtcNow - _lastSaveUtc >= TimeSpan.FromSeconds(30)) SaveHuntTrainRecord();
        return true;
    }

    public void SendTrainFlag(int index, bool openMap, bool showInChat, ushort textColor = 24, ushort flagColour = 559, ushort countColour = 502)
    {
        var train = HuntTrain;
        var valid = index >= 0 && index < train.Count;
        if (valid && openMap) OpenMap(train[index], true);
        if (!showInChat) return;
        var sb = new SeStringBuilder();
        if (!valid)
        {
            sb.AddUiForeground(textColor);
            sb.AddText("Nothing Left");
            sb.AddUiForegroundOff();
            _chatGui.Print(sb.BuiltString);
            return;
        }
        var mob = train[index];
        sb.AddUiForeground(textColor);
        sb.AddIcon(BitmapFontIcon.ExclamationRectangle);
        sb.AddText($"{mob.Name}{LocalizationUtil.GetInstanceGlyph(mob.Instance)}---");
        sb.AddUiForegroundOff();
        sb.AddUiForeground(flagColour);
        sb.Append(mob.MapLink);
        sb.AddUiForegroundOff();
        sb.AddUiForeground(countColour);
        sb.AddText($" --- {index + 1}/{train.Count}");
        sb.AddUiForegroundOff();
        _chatGui.Print(sb.BuiltString);
    }

    public void OpenMap(HuntTrainMob mob, bool openMap)
    {
        if (openMap) OpenMap((MapLinkPayload)mob.MapLink.Payloads[0]);
    }

    public void OpenMap(AetheryteData aeth, HuntTrainMob mob) =>
        OpenMap(new MapLinkPayload(mob.TerritoryID, mob.MapID, aeth.Position.X, aeth.Position.Y));

    public void OpenMap(MapLinkPayload mapLink) => _gameGui.OpenMapWithMapLink(mapLink);

    public void Import(string importCode) =>
        Import(ExportImport.Import(importCode, new List<HuntTrainMob>()));

    public List<HuntTrainMob> GetAllTrainRecords(IEnumerable<uint>? worldOrder = null)
    {
        var orderedWorlds = (worldOrder ?? Enumerable.Empty<uint>())
            .Concat(_trainsByWorld.Keys.OrderBy(id => id)).Distinct();
        return orderedWorlds.Where(_trainsByWorld.ContainsKey)
            .SelectMany(id => _trainsByWorld[id]).ToList();
    }

    public string ExportSelectedTrain() => ExportImport.Export(HuntTrain.ToList());

    public string ExportAllTrains(IEnumerable<uint>? worldOrder = null) =>
        ExportImport.Export(GetAllTrainRecords(worldOrder));

    // Missing WorldID (old codes / v1 IPC) inherits a captured destination.
    // Explicit world 0 and multi-world codes retain their original world boundaries.
    public void Import(IList<HuntTrainMob> trainMobs, uint? legacyWorldId = null)
    {
        var snapshot = trainMobs.ToList();
        var destinationId = legacyWorldId ?? SelectedWorldId;
        GetOrCreateTrain(destinationId);
        var destination = WorldWithCount(destinationId);
        ImportedTrain.Clear();
        foreach (var mob in snapshot)
        {
            if (mob == null) continue;
            var world = mob.HasWorldMetadata
                ? new HuntTrainWorld(mob.WorldID, mob.WorldName, mob.DataCenterName, 0)
                : destination;
            RememberWorld(world);
            ImportedTrain.Add(mob.WithWorld(world));
        }
        if (MapHelpers.DataManager != null) MapHelpers.LocaliseMobNames(ImportedTrain);
    }

    public void ImportTrainAll()
    {
        if (ImportedTrain.Count == 0) return;
        _suppressSave = true;
        try
        {
            foreach (var group in ImportedTrain.GroupBy(m => m.WorldID))
                GetOrCreateTrain(group.Key).ReplaceWith(group.Select(m => m.WithWorld(WorldWithCount(group.Key))));
        }
        finally { _suppressSave = false; }
        SaveHuntTrainRecord();
    }

    public void ImportTrainNew(bool updateOldTime)
    {
        if (ImportedTrain.Count == 0) return;
        _suppressSave = true;
        try
        {
            foreach (var mob in ImportedTrain)
            {
                var train = GetOrCreateTrain(mob.WorldID);
                var existing = train.FirstOrDefault(m => m.IsSameAs(mob.MobID, mob.Instance, mob.WorldID));
                if (existing == null) train.Add(mob.WithWorld(WorldWithCount(mob.WorldID)));
                else if (updateOldTime && mob.LastSeenUTC > existing.LastSeenUTC)
                {
                    existing.LastSeenUTC = mob.LastSeenUTC;
                    existing.Position = mob.Position;
                    existing.WorldPosition = mob.WorldPosition;
                }
            }
        }
        finally { _suppressSave = false; }
        SaveHuntTrainRecord();
    }

    public void TrainRemoveDead() => HuntTrain.RemoveAll(m => m.Dead);
    public bool ContainsMob(HuntTrainMob mob) =>
        _trainsByWorld.TryGetValue(mob.WorldID, out var train) &&
        train.Any(existing => existing.IsSameAs(mob.MobID, mob.Instance, mob.WorldID));
    public void TrainRemove(HuntTrainMob mob)
    {
        if (mob != null) HuntTrain.Remove(mob);
    }

    public void TrainUnkillAll()
    {
        HuntTrain.ForEach(m => m.Dead = false);
        SaveHuntTrainRecord();
    }

    public void DeleteWorld(uint worldId)
    {
        // Explicitly deleting a server must not recreate its tab on the next scan.
        // Starting recording again will rediscover any live nearby A rank.
        if (worldId == CurrentWorldId) RecordTrain = false;
        if (_trainsByWorld.TryGetValue(worldId, out var train)) train.Clear();
    }

    public void TrainDelete() => DeleteWorld(SelectedWorldId);
    public void TrainMarkAsDead(int index) => SetDead(index, true);

    public void SetDead(int index, bool dead)
    {
        var train = HuntTrain;
        if (index < 0 || index >= train.Count || train[index].Dead == dead) return;
        train[index].Dead = dead;
        SaveHuntTrainRecord();
    }

    public void SwapTrainMobs(int first, int second)
    {
        var train = HuntTrain;
        if (first < 0 || second < 0 || first >= train.Count || second >= train.Count || first == second) return;
        train.Swap(first, second);
    }

    public void LoadHuntTrainRecord()
    {
        if (!File.Exists(_huntTrainFilePath)) return;
        List<HuntTrainMob>? records;
        try
        {
            records = JsonConvert.DeserializeObject<List<HuntTrainMob>>(File.ReadAllText(_huntTrainFilePath));
            if (records == null) throw new JsonSerializationException("HuntTrain.json does not contain a train list.");
        }
        catch (Exception ex)
        {
            _canSave = false;
            PersistenceError = $"Unable to load hunt train data; original file preserved: {ex.Message}";
            PluginLog.Error(PersistenceError);
            return;
        }
        _canSave = true;
        PersistenceError = null;
        _suppressSave = true;
        try
        {
            foreach (var train in _trainsByWorld.Values) train.TrainChanged -= OnTrainChanged;
            _trainsByWorld.Clear();
            _worlds.Clear();
            foreach (var mob in records)
            {
                if (mob == null) continue;
                // Old local files deliberately stay world 0 instead of inheriting a login world.
                RememberWorld(new HuntTrainWorld(mob.WorldID, mob.WorldName, mob.DataCenterName, 0));
                GetOrCreateTrain(mob.WorldID).Add(mob);
            }
        }
        finally { _suppressSave = false; }
    }

    public void SaveHuntTrainRecord()
    {
        if (!_canSave) return;
        lock (_saveLock)
        {
            var tempPath = _huntTrainFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var serialised = JsonConvert.SerializeObject(_trainsByWorld.OrderBy(p => p.Key).SelectMany(p => p.Value).ToList(), Formatting.Indented);
                var directory = Path.GetDirectoryName(Path.GetFullPath(_huntTrainFilePath));
                if (directory != null) Directory.CreateDirectory(directory);
                File.WriteAllText(tempPath, serialised);
                if (File.Exists(_huntTrainFilePath)) File.Replace(tempPath, _huntTrainFilePath, null);
                else File.Move(tempPath, _huntTrainFilePath);
                _lastSaveUtc = DateTime.UtcNow;
                PersistenceError = null;
            }
            catch (Exception ex)
            {
                PersistenceError = $"Unable to save hunt train data: {ex.Message}";
                PluginLog.Error(PersistenceError);
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (Exception) { }
            }
        }
    }
}
