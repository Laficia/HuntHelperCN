using HuntHelper.Managers.Hunts.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Utilities;

/// <summary>Filters a server's existing records without changing their storage or indices.</summary>
public sealed class TrainVersionFilter
{
    private readonly Func<HuntTrainMob, int?> _aRankVersion;
    private readonly Dictionary<uint, int?> _selectedByWorld = new();
    public TrainVersionFilter(Func<HuntTrainMob, int?> aRankVersion) => _aRankVersion = aRankVersion;

    private int Category(HuntTrainMob mob) => _aRankVersion(mob) is { } version && version >= 2 ? version : 0;

    public List<TrainVersionGroup> Groups(IReadOnlyList<HuntTrainMob> records)
    {
        var result = records.GroupBy(Category).OrderByDescending(g => g.Key)
            .Select(g => new TrainVersionGroup(g.Key, g.Key == 0 ? "其它" : $"{g.Key}A", g.Count())).ToList();
        if (records.Count > 0) result.Add(new TrainVersionGroup(null, "全部", records.Count));
        return result;
    }

    public int? Selected(uint world, IReadOnlyList<HuntTrainMob> records)
    {
        var groups = Groups(records);
        if (groups.Count == 0) return null;
        if (_selectedByWorld.TryGetValue(world, out var selected) && groups.Any(g => g.Version == selected)) return selected;
        selected = groups[0].Version;
        _selectedByWorld[world] = selected;
        return selected;
    }

    public void Select(uint world, int? version) => _selectedByWorld[world] = version;
    public void Forget(uint world) => _selectedByWorld.Remove(world);

    public List<int> VisibleIndices(uint world, IReadOnlyList<HuntTrainMob> records)
    {
        var selected = Selected(world, records);
        return Enumerable.Range(0, records.Count).Where(i => selected == null || Category(records[i]) == selected).ToList();
    }

    public static int Clamp(int selected, IReadOnlyList<int> visible) => visible.Contains(selected) ? selected : visible.FirstOrDefault(0);

    public static int NextLiving(int selected, IReadOnlyList<int> visible, IReadOnlyList<HuntTrainMob> records)
    {
        var position = visible.ToList().IndexOf(selected);
        for (var offset = 1; offset < visible.Count; offset++)
        {
            var index = visible[(Math.Max(0, position) + offset) % visible.Count];
            if (!records[index].Dead) return index;
        }
        return -1;
    }
}
