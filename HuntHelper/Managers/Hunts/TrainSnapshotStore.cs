using HuntHelper.Managers.Hunts.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HuntHelper.Managers.Hunts;

public sealed class TrainSnapshot
{
    public int Version { get; set; } = 1;
    public string Id { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public uint[] Worlds { get; set; } = Array.Empty<uint>();
    public List<HuntTrainMob> Records { get; set; } = new();
}

/// <summary>Private per-module, server-scoped recovery snapshots. Never part of sharing codes.</summary>
public sealed class TrainSnapshotStore
{
    public const int Limit = 10;
    private readonly string _directory;
    private IReadOnlyList<TrainSnapshot>? _cache;
    public string? Error { get; private set; }
    public TrainSnapshotStore(string trainPath) => _directory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(trainPath))!,
        "Backups", Path.GetFileNameWithoutExtension(trainPath));

    public IReadOnlyList<TrainSnapshot> List()
    {
        if (_cache != null) return _cache;
        var snapshots = new List<TrainSnapshot>();
        try
        {
            if (!Directory.Exists(_directory)) return _cache = snapshots;
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                try
                {
                    var snapshot = JsonConvert.DeserializeObject<TrainSnapshot>(File.ReadAllText(file));
                    if (Valid(snapshot) && Path.GetFileNameWithoutExtension(file) == snapshot!.Id) snapshots.Add(snapshot);
                    else Error = "部分备份无效，已保留原文件";
                }
                catch (Exception) { Error = "部分备份无法读取，已保留原文件"; }
            }
        }
        catch (Exception ex) { Error = $"无法读取备份列表：{ex.Message}"; }
        return _cache = snapshots.OrderByDescending(s => s.CreatedUtc).ThenByDescending(s => s.Id).Take(Limit).ToList();
    }

    public bool Capture(string operation, uint[] worlds, List<HuntTrainMob> records)
    {
        var snapshot = new TrainSnapshot { Id = Guid.NewGuid().ToString("N"), CreatedUtc = DateTime.UtcNow,
            Operation = operation, Worlds = worlds.Distinct().ToArray(), Records = records };
        var temp = Path.Combine(_directory, snapshot.Id + ".tmp");
        try
        {
            if (!Valid(snapshot)) throw new InvalidDataException("备份内容无效");
            Directory.CreateDirectory(_directory);
            File.WriteAllText(temp, JsonConvert.SerializeObject(snapshot, Formatting.Indented));
            File.Move(temp, Path.Combine(_directory, snapshot.Id + ".json"));
            _cache = null;
            Error = null;
            // Rotate only valid files owned by this store. Invalid files remain available for manual recovery.
            var keep = List().Select(s => s.Id).ToHashSet();
            foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
            {
                try
                {
                    var old = JsonConvert.DeserializeObject<TrainSnapshot>(File.ReadAllText(file));
                    if (Valid(old) && Path.GetFileNameWithoutExtension(file) == old!.Id && !keep.Contains(old.Id)) File.Delete(file);
                }
                catch (Exception) { }
            }
            return true;
        }
        catch (Exception ex) { Error = $"自动备份失败，操作未执行：{ex.Message}"; return false; }
        finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { } }
    }

    public bool Read(string id, out TrainSnapshot? snapshot)
    {
        snapshot = null;
        if (!Guid.TryParseExact(id, "N", out _)) { Error = "备份编号无效"; return false; }
        try
        {
            snapshot = JsonConvert.DeserializeObject<TrainSnapshot>(File.ReadAllText(Path.Combine(_directory, id + ".json")));
            if (!Valid(snapshot) || snapshot!.Id != id) throw new InvalidDataException("备份内容无效");
            Error = null; return true;
        }
        catch (Exception ex) { snapshot = null; Error = $"无法恢复备份，现有记录保留：{ex.Message}"; return false; }
    }

    private static bool Valid(TrainSnapshot? s) => s is { Version: 1 } && Guid.TryParseExact(s.Id, "N", out _) &&
        s.CreatedUtc != default && !string.IsNullOrWhiteSpace(s.Operation) &&
        s.Worlds is { Length: > 0 } && s.Records != null && s.Records.All(m => m != null && m.HasWorldMetadata &&
            s.Worlds.Contains(m.WorldID) && float.IsFinite(m.Position.X) && float.IsFinite(m.Position.Y));
}
