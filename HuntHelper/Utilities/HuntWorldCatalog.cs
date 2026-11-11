using HuntHelper.Managers.Hunts.Models;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Utilities;

public sealed record HuntWorldCatalogEntry(uint WorldId, string Name, uint DataCenterId, string DataCenterName, uint Region, bool IsPublic);

/// <summary>The CN sheet marks active worlds IsPublic=false; valid CN DC assignment identifies them.</summary>
public static class HuntWorldCatalog
{
    public static IReadOnlyList<HuntTrainWorld> Select(IEnumerable<HuntWorldCatalogEntry> rows, uint currentWorld, bool chineseClient)
    {
        var entries = rows.ToList();
        var current = entries.FirstOrDefault(w => w.WorldId == currentWorld);
        var china = chineseClient || current?.Region == 5;
        if (!china && (current == null || current.Region == 0)) return new List<HuntTrainWorld>();
        return entries.Where(w => w.WorldId != 0 && w.DataCenterId != 0 &&
                !string.IsNullOrWhiteSpace(w.Name) && !string.IsNullOrWhiteSpace(w.DataCenterName) &&
                (china ? w.Region == 5 && w.WorldId >= 1000 : w.IsPublic && w.Region == current!.Region))
            .DistinctBy(w => w.WorldId).OrderBy(w => w.DataCenterId).ThenBy(w => w.WorldId)
            .Select(w => new HuntTrainWorld(w.WorldId, w.Name, w.DataCenterName, 0)).ToList();
    }
}
