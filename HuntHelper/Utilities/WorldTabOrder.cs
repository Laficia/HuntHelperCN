using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Utilities;

public static class WorldTabOrder
{
    public static bool Synchronize(List<uint> order, IEnumerable<uint> visibleWorlds)
    {
        var visible = visibleWorlds.Distinct().ToArray();
        var updated = order.Where(visible.Contains).Distinct().ToList();
        updated.AddRange(visible.Where(id => !updated.Contains(id)));
        if (order.SequenceEqual(updated)) return false;
        order.Clear();
        order.AddRange(updated);
        return true;
    }

    public static bool MoveRelative(List<uint> order, uint source, uint target, bool after)
    {
        if (source == target || !order.Contains(source) || !order.Contains(target)) return false;
        var updated = order.ToList();
        updated.Remove(source);
        updated.Insert(updated.IndexOf(target) + (after ? 1 : 0), source);
        if (order.SequenceEqual(updated)) return false;
        order.Clear();
        order.AddRange(updated);
        return true;
    }

    public static bool MoveBy(List<uint> order, uint world, int direction)
    {
        var index = order.IndexOf(world);
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= order.Count || target == index) return false;
        (order[index], order[target]) = (order[target], order[index]);
        return true;
    }
}
