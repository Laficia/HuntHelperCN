using System;
using System.Collections.Concurrent;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using KamiToolKit;
using KamiToolKit.Classes;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace HuntHelper.Gui.Native;

internal sealed class NativeTrainRuntime : IDisposable
{
    private sealed class Slot
    {
        internal NativeTrainSnapshot? Snapshot;
        internal NativeTrainWindow? Window;
        internal bool UserClosed;
    }
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly Task init;
    private readonly Slot a = new(), s = new();
    private readonly ConcurrentQueue<(string Rank, NativeTrainRequest Request)> requests = new();
    private readonly ConcurrentQueue<(string Rank, Vector2 Size)> sizes = new();
    private readonly Vector2 aSize, sSize;
    private long nextUpdate;
    private bool disposed;
    private int theme = -1;
    private Vector4 textColor, buttonColor;
    internal bool Failed { get; private set; }

    internal NativeTrainRuntime(IDalamudPluginInterface pi, IFramework framework, IPluginLog log, Vector2 aSize, Vector2 sSize)
    {
        this.framework = framework; this.log = log; this.aSize = aSize; this.sSize = sSize;
        init = KamiToolKitLibrary.InitializeAsync(pi);
        framework.Update += OnUpdate;
    }

    internal void Publish(NativeTrainSnapshot aSnapshot, NativeTrainSnapshot sSnapshot)
    { Volatile.Write(ref a.Snapshot, aSnapshot); Volatile.Write(ref s.Snapshot, sSnapshot); }

    internal void Drain(HuntTrainUI aUi, HuntTrainUI sUi, Action<string, Vector2> saveSize)
    {
        for (int i = 0; i < 64 && requests.TryDequeue(out var next); i++)
            (next.Rank == "S" ? sUi : aUi).NativeDispatch(next.Request);
        while (sizes.TryDequeue(out var next)) saveSize(next.Rank, next.Size);
    }

    private void OnUpdate(IFramework unused)
    {
        if (disposed || Failed || !init.IsCompleted || Environment.TickCount64 < nextUpdate) return;
        nextUpdate = Environment.TickCount64 + 160;
        if (!init.IsCompletedSuccessfully)
        { Failed = true; log.Error(init.Exception!, "[HHT Native] KamiToolKit 初始化失败，恢复原窗口"); return; }
        try { RefreshTheme(); Tick(a, "A", aSize); Tick(s, "S", sSize); }
        catch (Exception ex) { Failed = true; log.Error(ex, "[HHT Native] 原生窗口异常，恢复原窗口"); a.Window?.CloseFromModel(); s.Window?.CloseFromModel(); }
    }

    private unsafe void RefreshTheme()
    {
        var stage = AtkStage.Instance();
        if (stage == null || stage->AtkUIColorHolder == null) return;
        var nextTheme = (int)stage->AtkUIColorHolder->ActiveColorThemeType;
        var nextText = ColorHelper.GetColor(8);
        var nextButton = ColorHelper.GetColor(50);
        if (theme == nextTheme && textColor == nextText && buttonColor == nextButton) return;
        theme = nextTheme; textColor = nextText; buttonColor = nextButton;
        ColorHelper.RefreshColorTheme();
    }

    private void Tick(Slot slot, string rank, Vector2 size)
    {
        var snapshot = Volatile.Read(ref slot.Snapshot);
        if (snapshot == null) return;
        if (!snapshot.Visible) slot.UserClosed = false;
        if (slot.Window == null && snapshot.Visible)
            slot.Window = new NativeTrainWindow(request => {
                if (request.Action == NativeTrainAction.Close) slot.UserClosed = true;
                requests.Enqueue((rank, request));
            }, changed => sizes.Enqueue((rank, changed))) {
                InternalName = rank == "A" ? "HHTNativeTrain" : "HHSNativeTrain",
                Title = rank == "A" ? "狩猎车" : "S怪记录",
                Size = Vector2.Clamp(size, new(350, 300), new(1500, 1400)) };
        if (slot.Window == null) return;
        slot.Window.Apply(snapshot);
        if (snapshot.Visible && !slot.UserClosed && !slot.Window.IsOpen) slot.Window.Open();
        else if (!snapshot.Visible && slot.Window.IsOpen) slot.Window.CloseFromModel();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true; framework.Update -= OnUpdate;
        void Cleanup()
        {
            a.Window?.Dispose(); s.Window?.Dispose(); KamiToolKitLibrary.Dispose();
        }
        // The library's synchronous disposal restores native vtables before nodes are freed.
        // Do not block the framework on its own scheduler.
        if (init.IsCompletedSuccessfully)
        {
            if (framework.IsInFrameworkUpdateThread) Cleanup();
            else framework.RunOnFrameworkThread(Cleanup).GetAwaiter().GetResult();
        }
        else if (!init.IsCompleted)
            _ = init.ContinueWith(task => { if (task.IsCompletedSuccessfully) _ = framework.RunOnFrameworkThread(Cleanup); }, TaskScheduler.Default);
    }
}
