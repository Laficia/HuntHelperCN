using HuntHelper.Managers.Hunts.Models;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Utilities;

// Expected species are presentation data, never fabricated HuntTrainMob records.
public sealed record HuntRosterEntry(int Version, uint NameId, string Name);
public readonly record struct HuntRosterProgress(int Found, int Total);

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
}
