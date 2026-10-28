using HuntHelper.Managers.Hunts.Models;
using System.Collections.Generic;

namespace HuntHelper.Utilities;

/// <summary>Default insertion order for newly recorded A ranks; never sorts existing rows.</summary>
public static class ARankMapOrder
{
    private static readonly Dictionary<uint, (int Version, int Position)> Maps = Build();
    private static Dictionary<uint, (int, int)> Build()
    {
        var maps = new Dictionary<uint, (int, int)>();
        void Add(int version, params uint[] territories)
        { for (var i = 0; i < territories.Length; i++) maps.Add(territories[i], (version, i)); }
        // TerritoryType IDs, verified against Data/SpawnPointData.json.
        // Fringes, Peaks, Lochs, Ruby Sea, Yanxia, Azim Steppe.
        Add(4, 612, 620, 621, 613, 614, 622);
        // Lakeland, Kholusia, Amh Araeng, Il Mheg, Rak'tika, Tempest.
        Add(5, 813, 814, 815, 816, 817, 818);
        // Thavnair, Labyrinthos, Garlemald, Mare, Elpis, Ultima Thule.
        Add(6, 957, 956, 958, 959, 961, 960);
        // Urqopacha, Kozama'uka, Yak T'el, Shaaloani, Heritage Found, Living Memory.
        Add(7, 1187, 1188, 1189, 1190, 1191, 1192);
        return maps;
    }

    public static int InsertionIndex(IReadOnlyList<HuntTrainMob> records, HuntTrainMob incoming)
    {
        if (!Maps.TryGetValue(incoming.TerritoryID, out var key)) return records.Count;
        var sameMap = -1;
        var firstLater = -1;
        var lastVersion = -1;
        for (var i = 0; i < records.Count; i++)
        {
            // The caller supplies a world-specific list; also reject any foreign row here.
            if (records[i].WorldID != incoming.WorldID || !Maps.TryGetValue(records[i].TerritoryID, out var existing) ||
                existing.Version != key.Version) continue;
            lastVersion = i;
            if (existing.Position == key.Position) sameMap = i;
            if (existing.Position > key.Position && firstLater < 0) firstLater = i;
        }
        // Keep discoveries from the same map together, stable across monsters and instances.
        // A player's earlier drag remains intact: only insert the new object, never reorder old ones.
        return sameMap >= 0 ? sameMap + 1 : firstLater >= 0 ? firstLater : lastVersion >= 0 ? lastVersion + 1 : records.Count;
    }
}
