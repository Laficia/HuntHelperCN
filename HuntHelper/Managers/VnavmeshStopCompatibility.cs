using Dalamud.Plugin.Services;
using HuntHelper.Utilities;
using System;

namespace HuntHelper.Managers;

public sealed class VnavmeshStopCompatibility(ICommandManager commands)
{
    public Action? Resolve()
    {
        if (!commands.Commands.TryGetValue("/vnav", out var info) || info.Handler.Target is not { } target ||
            target.GetType().Assembly.GetName().Name != "vnavmesh") return null;
        return VnavmeshPendingStop.Resolve(target);
    }
}
