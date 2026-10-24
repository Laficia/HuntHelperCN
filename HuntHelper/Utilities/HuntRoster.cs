using HuntHelper.Managers.Hunts.Models;
using System.Collections.Generic;
using System;
using System.Linq;

namespace HuntHelper.Utilities;

// Expected species are presentation data, never fabricated HuntTrainMob records.
public sealed record HuntRosterEntry(int Version, uint NameId, string Name, uint TerritoryId = 0);
public readonly record struct HuntRosterProgress(int Found, int Total);
public sealed record HuntRosterSlot(HuntRosterEntry Entry, uint TerritoryId, uint Instance);
public sealed record HuntRosterSnapshot(HuntRosterProgress Progress, IReadOnlyList<HuntRosterSlot> Missing);

public sealed class HuntRoster
{
    public IReadOnlyList<HuntRosterEntry> Entries { get; }
    public HuntRoster(IEnumerable<HuntRosterEntry> entries) => Entries = entries
        .Where(e => e.Version >= 2 && e.NameId != 0).DistinctBy(e => (e.Version, e.NameId)).ToList();

    public List<HuntRosterEntry> Missing(uint world, int? version, IReadOnlyList<HuntTrainMob> records)
    {
        if (version == null || version < 2) return new();
        var found = records.Where(m => m.WorldID == world).Select(m => m.MobID).ToHashSet();
        return Entries.Where(e => e.Version == version && !found.Contains(e.NameId)).ToList();
    }

    public HuntRosterProgress Progress(uint world, int version, IReadOnlyList<HuntTrainMob> records)
    {
        var expected = Entries.Where(e => e.Version == version).Select(e => e.NameId).ToHashSet();
        var count = records.Where(m => m.WorldID == world && expected.Contains(m.MobID)).Select(m => m.MobID).Distinct().Count();
        return new(count, expected.Count);
    }

    public HuntRosterSnapshot Snapshot(uint world, int version, IReadOnlyList<HuntTrainMob> records,
        Func<uint, IReadOnlyList<uint>> instances)
    {
        var missing = new List<HuntRosterSlot>();
        var total = 0;
        var owned = records.Where(m => m.WorldID == world).ToList();
        foreach (var entry in Entries.Where(e => e.Version == version))
        {
            var territory = entry.TerritoryId != 0 ? entry.TerritoryId
                : owned.FirstOrDefault(m => m.MobID == entry.NameId && m.TerritoryID != 0)?.TerritoryID ?? 0;
            var lines = instances(territory).Where(i => i <= 9).Distinct().ToArray();
            if (lines.Length == 0) lines = new uint[] { 0 };
            foreach (var line in lines)
            {
                total++;
                if (!owned.Any(m => m.MobID == entry.NameId && (territory == 0 || m.TerritoryID == territory) && m.Instance == line))
                    missing.Add(new(entry, territory, line));
            }
        }
        return new(new(total - missing.Count, total), missing);
    }
}
