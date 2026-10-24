using HuntHelper.Managers.Hunts.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Utilities;

/// <summary>Includes servers without train records, refreshing the game-data catalog when the visited world changes.</summary>
public sealed class HuntRelayWorldCatalog
{
    private readonly Func<uint, IReadOnlyList<HuntTrainWorld>> _loadPublicWorlds;
    private uint _visited;
    private IReadOnlyList<HuntTrainWorld> _public = Array.Empty<HuntTrainWorld>();

    public HuntRelayWorldCatalog(Func<uint, IReadOnlyList<HuntTrainWorld>> loadPublicWorlds) => _loadPublicWorlds = loadPublicWorlds;

    public IReadOnlyList<HuntTrainWorld> Get(uint visited, IEnumerable<HuntTrainWorld> known)
    {
        if (visited == 0) return Array.Empty<HuntTrainWorld>();
        if (_visited != visited || _public.Count == 0)
        {
            _public = _loadPublicWorlds(visited);
            _visited = visited;
        }
        return _public.Concat(known).Where(w => w.WorldID != 0).DistinctBy(w => w.WorldID).ToArray();
    }
}
