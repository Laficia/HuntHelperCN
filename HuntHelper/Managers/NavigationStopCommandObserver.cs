using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using HuntHelper.Utilities;
using System;

namespace HuntHelper.Managers;

/// <summary>Observe only an explicit /vnav stop, while HHT owns a route.
/// Keep the command intact and pass it through the original game handler.</summary>
public sealed unsafe class NavigationStopCommandObserver : IDisposable
{
    private delegate void ChatEntry(UIModule* module, Utf8String* text, nint unused, bool saveHistory);
    private readonly Hook<ChatEntry> _hook;
    private readonly NavigationManager _navigation;
    public NavigationStopCommandObserver(IGameInteropProvider interop, NavigationManager navigation)
    {
        _navigation = navigation;
        _hook = interop.HookFromAddress<ChatEntry>(UIModule.Addresses.ProcessChatBoxEntry.Value, Detour);
        _hook.Enable();
    }
    private void Detour(UIModule* module, Utf8String* text, nint unused, bool saveHistory)
    {
        try
        {
            if (_navigation.OwnsMovement && text != null && VnavmeshPendingStop.IsStopCommand(text->ToString()))
                _navigation.NotifyStopCommand();
        }
        catch (Exception ex) { PluginLog.Warning(ex, "Could not observe owned vnav stop command"); }
        _hook.Original(module, text, unused, saveHistory);
    }
    public void Dispose() => _hook.Dispose();
}
