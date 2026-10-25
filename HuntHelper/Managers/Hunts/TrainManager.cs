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

public enum RelayRecordResult { Ignored, Added, Updated }

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
    private readonly Func<HuntTrainMob, bool> _acceptRecord;
    private readonly string _huntTrainFilePath;
    private readonly Dictionary<uint, TrainList<HuntTrainMob>> _trainsByWorld = new();
    private readonly Dictionary<uint, HuntTrainWorld> _worlds = new();
    private readonly object _saveLock = new();
    private uint _selectedWorldId;
    private bool _selectionChosen;
    private bool _suppressSave;
    private bool _canSave = true;
    private DateTime _lastSaveUtc;
    public TrainSnapshotStore Snapshots { get; }
    public string? ActionError { get; private set; }

    public TrainList<HuntTrainMob> HuntTrain => GetOrCreateTrain(SelectedWorldId);
    public readonly List<HuntTrainMob> ImportedTrain = new();
    public bool RecordTrain = false;
    public bool ImportFromIPC = false;
    public string? PersistenceError { get; private set; }
    public event Action<HuntTrainMob>? ManualDeadStateChanged;
    public event Action<uint>? WorldDeleted;

    public IReadOnlyList<HuntTrainMob> GetTrainRecords(uint worldId) =>
        _trainsByWorld.TryGetValue(worldId, out var records) ? records : Array.Empty<HuntTrainMob>();

    public uint SelectedWorldId
    {
        get
        {
            if (!_selectionChosen) GetCurrentWorld();
            return _selectedWorldId;
        }
    }

    public uint CurrentWorldId => GetCurrentWorld().WorldID;

    public TrainManager(IChatGui chatGui, IGameGui gameGui, string huntTrainFilePath, IPlayerState playerState,
        Func<HuntTrainMob, bool>? acceptRecord = null)
        : this(chatGui, gameGui, huntTrainFilePath, () => ReadCurrentWorld(playerState), acceptRecord)
    {
    }

    public TrainManager(IChatGui chatGui, IGameGui gameGui, string huntTrainFilePath, Func<HuntTrainWorld> currentWorldProvider,
        Func<HuntTrainMob, bool>? acceptRecord = null)
    {
        _chatGui = chatGui;
        _gameGui = gameGui;
        _huntTrainFilePath = huntTrainFilePath;
        _currentWorldProvider = currentWorldProvider;
        _acceptRecord = acceptRecord ?? (_ => true);
        Snapshots = new TrainSnapshotStore(huntTrainFilePath);
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
        if (!_acceptRecord(mob)) return;
        var world = GetCurrentWorld();
        var train = GetOrCreateTrain(world.WorldID);
        if (train.Any(m => m.IsSameAs(mob.MobID, mob.Instance, world.WorldID))) return;
        train.Add(mob.WithWorld(world));
    }

    public RelayRecordResult RecordRelay(HuntTrainMob mob)
    {
        if (!RecordTrain || !_acceptRecord(mob) || !mob.HasWorldMetadata || mob.WorldID == 0 || mob.TerritoryID == 0 || mob.MapID == 0 ||
            !float.IsFinite(mob.Position.X) || !float.IsFinite(mob.Position.Y)) return RelayRecordResult.Ignored;
        mob.Source = HuntRecordSource.Sonar;
        mob.SonarReceivedUtc ??= mob.LastSeenUTC;
        mob.RecordedAtUtc ??= mob.SonarReceivedUtc;
        RememberWorld(new(mob.WorldID, mob.WorldName, mob.DataCenterName, 0));
        var train = GetOrCreateTrain(mob.WorldID);
        var existing = train.FirstOrDefault(m => m.IsSameAs(mob.MobID, mob.Instance, mob.WorldID) && m.TerritoryID == mob.TerritoryID);
        if (existing == null) { train.Add(mob); return RelayRecordResult.Added; }
        if (!existing.WorldPosition.HasValue || System.Numerics.Vector2.DistanceSquared(existing.Position, mob.Position) > .2f * .2f)
        { existing.Position = mob.Position; existing.WorldPosition = null; existing.Source = HuntRecordSource.Sonar; }
        if (!existing.SonarReceivedUtc.HasValue || mob.SonarReceivedUtc > existing.SonarReceivedUtc)
            existing.SonarReceivedUtc = mob.SonarReceivedUtc;
        if (existing.Source == HuntRecordSource.Unknown) existing.Source = HuntRecordSource.Sonar;
        if (mob.LastSeenUTC > existing.LastSeenUTC) existing.LastSeenUTC = mob.LastSeenUTC;
        // A map-only announcement cannot revive a dead record or fabricate a height.
        SaveHuntTrainRecord();
        return RelayRecordResult.Updated;
    }

    public bool UpdateLastSeen(IBattleNpc mob, uint instance, float? zoneMapCoordSize = null)
    {
        var world = GetCurrentWorld();
        var existing = GetOrCreateTrain(world.WorldID).FirstOrDefault(m => m.IsSameAs(mob.NameId, instance, world.WorldID));
        if (existing == null) return false;
        existing.LastSeenUTC = DateTime.UtcNow;
        existing.Source = HuntRecordSource.Scan;
        existing.LastObservedUtc = existing.LastSeenUTC;
        existing.RecordedAtUtc ??= existing.LastSeenUTC;
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
            sb.AddText("没有下一只记录");
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
            if (mob == null || !_acceptRecord(mob)) continue;
            var world = mob.HasWorldMetadata
                ? new HuntTrainWorld(mob.WorldID, mob.WorldName, mob.DataCenterName, 0)
                : destination;
            RememberWorld(world);
            var imported = mob.WithWorld(world);
            imported.Source = HuntRecordSource.Import;
            imported.ImportedAtUtc = DateTime.UtcNow;
            imported.RecordedAtUtc ??= imported.ImportedAtUtc;
            ImportedTrain.Add(imported);
        }
        if (MapHelpers.DataManager != null) MapHelpers.LocaliseMobNames(ImportedTrain);
    }

    public bool ImportTrainAll()
    {
        if (ImportedTrain.Count == 0) return false;
        return Change("覆盖导入", ImportedTrain.Select(m => m.WorldID), () =>
        {
            foreach (var group in ImportedTrain.GroupBy(m => m.WorldID))
                GetOrCreateTrain(group.Key).ReplaceWith(group.Select(m => ImportCopy(m, WorldWithCount(group.Key))));
        });
    }

    public bool ImportTrainNew(bool updateOldTime)
    {
        if (ImportedTrain.Count == 0) return false;
        return Change("合并导入", ImportedTrain.Select(m => m.WorldID), () =>
        {
            foreach (var mob in ImportedTrain)
            {
                var train = GetOrCreateTrain(mob.WorldID);
                var existing = train.FirstOrDefault(m => m.IsSameAs(mob.MobID, mob.Instance, mob.WorldID));
                if (existing == null) train.Add(ImportCopy(mob, WorldWithCount(mob.WorldID)));
                else if (updateOldTime && mob.LastSeenUTC > existing.LastSeenUTC)
                {
                    existing.LastSeenUTC = mob.LastSeenUTC;
                    existing.Position = mob.Position;
                    existing.WorldPosition = mob.WorldPosition;
                    existing.Source = HuntRecordSource.Import;
                    existing.ImportedAtUtc = DateTime.UtcNow;
                    existing.LastObservedUtc = mob.LastObservedUtc;
                    existing.SonarReceivedUtc = mob.SonarReceivedUtc;
                }
            }
        });
    }

    public void TrainRemoveDead() => RemoveRecords(HuntTrain.Where(m => m.Dead));

    private static HuntTrainMob ImportCopy(HuntTrainMob mob, HuntTrainWorld world)
    {
        var copy = mob.WithWorld(world);
        copy.Source = HuntRecordSource.Import;
        copy.ImportedAtUtc = DateTime.UtcNow;
        copy.RecordedAtUtc ??= copy.ImportedAtUtc;
        return copy;
    }
    public void RemoveRecords(IEnumerable<HuntTrainMob> records, bool pauseRecording = false)
    {
        var selected = records.Where(HuntTrain.Contains).ToHashSet();
        if (selected.Count == 0) return;
        if (!Change(pauseRecording ? "清空标签" : "删除记录", new[] { SelectedWorldId }, () => HuntTrain.RemoveAll(selected.Contains))) return;
        if (pauseRecording && SelectedWorldId == CurrentWorldId)
        {
            WorldDeleted?.Invoke(SelectedWorldId);
            RecordTrain = false;
        }
    }

    public int RestoreRecords(IEnumerable<HuntTrainMob> records)
    {
        var restored = records.Where(HuntTrain.Contains).Where(m => m.Dead).Distinct().ToList();
        if (restored.Count == 0) return 0;
        if (!Change("恢复存活", new[] { SelectedWorldId }, () => restored.ForEach(m => m.Dead = false))) return 0;
        foreach (var mob in restored) ManualDeadStateChanged?.Invoke(mob);
        return restored.Count;
    }
    public bool ContainsMob(HuntTrainMob mob) =>
        _trainsByWorld.TryGetValue(mob.WorldID, out var train) &&
        train.Any(existing => existing.IsSameAs(mob.MobID, mob.Instance, mob.WorldID));
    public void TrainRemove(HuntTrainMob mob)
    {
        if (mob != null) RemoveRecords(new[] { mob });
    }

    public void TrainUnkillAll()
    {
        RestoreRecords(HuntTrain);
    }

    public bool DeleteWorld(uint worldId)
    {
        if (!_trainsByWorld.TryGetValue(worldId, out var train) || train.Count == 0) return false;
        if (!Change("删除服务器", new[] { worldId }, train.Clear)) return false;
        WorldDeleted?.Invoke(worldId);
        // Explicitly deleting a server must not recreate its tab on the next scan.
        // Starting recording again will rediscover any live nearby A rank.
        if (worldId == CurrentWorldId) RecordTrain = false;
        return true;
    }

    public void TrainDelete() => DeleteWorld(SelectedWorldId);
    public void TrainMarkAsDead(int index) => SetDead(index, true);

    public void SetDead(int index, bool dead)
    {
        ActionError = null;
        var train = HuntTrain;
        if (index < 0 || index >= train.Count || train[index].Dead == dead) return;
        if (!Change(dead ? "标记死亡" : "恢复存活", new[] { SelectedWorldId }, () => train[index].Dead = dead)) return;
        ManualDeadStateChanged?.Invoke(train[index]);
    }

    public void SwapTrainMobs(int first, int second)
    {
        var train = HuntTrain;
        if (first < 0 || second < 0 || first >= train.Count || second >= train.Count || first == second) return;
        Change("调整车序", new[] { SelectedWorldId }, () => train.Swap(first, second));
    }

    private static List<HuntTrainMob> Clone(IEnumerable<HuntTrainMob> records) =>
        JsonConvert.DeserializeObject<List<HuntTrainMob>>(JsonConvert.SerializeObject(records))!;

    private void ReplaceWorlds(uint[] worlds, IEnumerable<HuntTrainMob> records)
    {
        var rows = records.ToList();
        foreach (var mob in rows) RememberWorld(new(mob.WorldID, mob.WorldName, mob.DataCenterName, 0));
        foreach (var world in worlds) GetOrCreateTrain(world).ReplaceWith(rows.Where(m => m.WorldID == world));
    }

    private bool Change(string operation, IEnumerable<uint> worldIds, Action change)
    {
        ActionError = null;
        if (!_canSave) { ActionError = "记录文件异常，未执行操作；请先保留原文件并排查"; return false; }
        var worlds = worldIds.Distinct().ToArray();
        var before = Clone(GetAllTrainRecords().Where(m => worlds.Contains(m.WorldID)));
        if (!Snapshots.Capture(operation, worlds, before)) { ActionError = Snapshots.Error; return false; }
        _suppressSave = true;
        try
        {
            change();
            if (SaveHuntTrainRecord()) return true;
            ReplaceWorlds(worlds, before);
            ActionError = "保存失败，已撤销本次修改：" + PersistenceError;
            return false;
        }
        catch (Exception ex)
        {
            ReplaceWorlds(worlds, before);
            ActionError = "操作失败，已保留原记录：" + ex.Message;
            return false;
        }
        finally { _suppressSave = false; }
    }

    public bool RestoreSnapshot(string id)
    {
        if (!Snapshots.Read(id, out var snapshot)) { ActionError = Snapshots.Error; return false; }
        if (snapshot!.Records.Any(m => !_acceptRecord(m))) { ActionError = "备份包含不属于本模块的记录，未执行恢复"; return false; }
        if (!Change("恢复备份前", snapshot.Worlds, () => ReplaceWorlds(snapshot.Worlds, snapshot.Records))) return false;
        if (snapshot.Worlds.Contains(CurrentWorldId)) { RecordTrain = false; WorldDeleted?.Invoke(CurrentWorldId); }
        foreach (var world in snapshot.Worlds)
            foreach (var mob in GetTrainRecords(world)) ManualDeadStateChanged?.Invoke(mob);
        return true;
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
            PersistenceError = $"无法读取狩猎记录，已保留原文件：{ex.Message}";
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
                if (mob == null || !_acceptRecord(mob)) continue;
                // Old local files deliberately stay world 0 instead of inheriting a login world.
                RememberWorld(new HuntTrainWorld(mob.WorldID, mob.WorldName, mob.DataCenterName, 0));
                GetOrCreateTrain(mob.WorldID).Add(mob);
            }
        }
        finally { _suppressSave = false; }
    }

    public bool SaveHuntTrainRecord()
    {
        if (!_canSave) return false;
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
                return true;
            }
            catch (Exception ex)
            {
                PersistenceError = $"无法保存狩猎记录：{ex.Message}";
                PluginLog.Error(PersistenceError);
                return false;
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (Exception) { }
            }
        }
    }
}
