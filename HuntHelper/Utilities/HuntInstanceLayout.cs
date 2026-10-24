using HuntHelper.Managers.Hunts.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Utilities;

/// <summary>Per-server, per-territory display rules. Never switches instances or edits records.</summary>
public sealed class HuntInstanceLayout
{
    private readonly Dictionary<uint, Dictionary<uint, int>> _overrides;
    private readonly Dictionary<(uint World, uint Territory), uint> _observed = new();

    public HuntInstanceLayout(Dictionary<uint, Dictionary<uint, int>> overrides) => _overrides = overrides;

    public void Observe(uint world, uint territory, uint instance)
    {
        if (world == 0 || territory == 0 || instance > 9) return;
        var key = (world, territory);
        // A ready live observation of instance zero means this map is currently uninstanced.
        _observed[key] = instance == 0 ? 0 : Math.Max(instance, _observed.GetValueOrDefault(key));
    }

    public int Mode(uint world, uint territory) =>
        _overrides.TryGetValue(world, out var maps) ? maps.GetValueOrDefault(territory) : 0;

    public void SetMode(uint world, uint territory, int count)
    {
        if (world == 0 || territory == 0 || count is < 0 or > 9) return;
        if (count == 0)
        {
            if (_overrides.TryGetValue(world, out var maps))
            { maps.Remove(territory); if (maps.Count == 0) _overrides.Remove(world); }
            return;
        }
        if (!_overrides.TryGetValue(world, out var target)) _overrides[world] = target = new();
        target[territory] = count;
    }

    public IReadOnlyList<uint> Instances(uint world, uint territory, IReadOnlyList<HuntTrainMob> records)
    {
        if (territory == 0) return new uint[] { 0 };
        var count = Mode(world, territory);
        if (count is >= 1 and <= 9) return Lines(count);
        if (_observed.TryGetValue((world, territory), out var observed) && observed == 0)
            return new uint[] { 0 };
        // Records establish a lower bound, not a claim about an unseen server-wide instance count.
        var highest = records.Where(m => m.WorldID == world && m.TerritoryID == territory && m.Instance <= 9)
            .Select(m => m.Instance).DefaultIfEmpty(0u).Max();
        highest = Math.Max(highest, observed);
        if (highest > 0) _observed[(world, territory)] = highest;
        return highest == 0 ? new uint[] { 0 } : Enumerable.Range(1, (int)highest).Select(i => (uint)i).ToArray();
    }

    private static uint[] Lines(int count) => count == 1 ? new uint[] { 0 }
        : Enumerable.Range(1, count).Select(i => (uint)i).ToArray();
}
