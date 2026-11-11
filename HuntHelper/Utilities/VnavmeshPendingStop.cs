using System;
using System.Linq;
using System.Reflection;

namespace HuntHelper.Utilities;

/// <summary>Compatibility with CN builds whose Path.Stop only stops the executor.
/// Resolve from the loaded /vnav handler, never load a second navmesh assembly or change its settings.</summary>
public static class VnavmeshPendingStop
{
    public static Action? Resolve(object commandTarget)
    {
        var fields = commandTarget.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        var move = fields.FirstOrDefault(f => f.FieldType.Name == "AsyncMoveRequest")?.GetValue(commandTarget);
        if (move == null) return null;
        var stop = move.GetType().GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes)
            ?? move.GetType().GetMethod("CancelPathfinding", BindingFlags.Instance | BindingFlags.Public, Type.EmptyTypes);
        return stop is { ReturnType: var result } && result == typeof(void) ? () => stop.Invoke(move, null) : null;
    }

    public static bool IsStopCommand(string text)
    {
        var parts = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0].Equals("/vnav", StringComparison.OrdinalIgnoreCase) && parts[1].Equals("stop", StringComparison.OrdinalIgnoreCase);
    }
}
