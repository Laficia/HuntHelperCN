using System.Numerics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using HuntHelper.Managers;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Lumina.Excel.Sheets;

namespace HuntHelper.NavigationTests;

internal sealed class FakePlugin : IDalamudPluginInterface
{
    internal sealed class Endpoint
    {
        public Delegate? Function { get; set; }
        public Delegate? Action { get; set; }
        public int Calls { get; set; }
    }

    private readonly Dictionary<string, Endpoint> _endpoints = new();
    public bool Available { get; set; } = true;
    public int TotalCalls => _endpoints.Values.Sum(endpoint => endpoint.Calls);

    public Endpoint At(string name)
    {
        if (!_endpoints.TryGetValue(name, out var endpoint)) _endpoints.Add(name, endpoint = new());
        return endpoint;
    }

    public ICallGateSubscriber<TRet> GetIpcSubscriber<TRet>(string name) => new Gate0<TRet>(this, At(name));
    public ICallGateSubscriber<T1, T2, TRet> GetIpcSubscriber<T1, T2, TRet>(string name) => new Gate2<T1, T2, TRet>(this, At(name));
    public ICallGateSubscriber<T1, T2, T3, TRet> GetIpcSubscriber<T1, T2, T3, TRet>(string name) => new Gate3<T1, T2, T3, TRet>(this, At(name));
    public ICallGateSubscriber<T1, T2, T3, T4, TRet> GetIpcSubscriber<T1, T2, T3, T4, TRet>(string name) => new Gate4<T1, T2, T3, T4, TRet>(this, At(name));

    private abstract class GateBase(FakePlugin plugin, Endpoint endpoint) : ICallGateSubscriber
    {
        public bool HasFunction => plugin.Available && endpoint.Function != null;
        public bool HasAction => plugin.Available && endpoint.Action != null;

        protected object? InvokeFunction(params object?[] arguments)
        {
            endpoint.Calls++;
            if (!HasFunction) throw new MissingMethodException("Function IPC is unavailable");
            return endpoint.Function!.DynamicInvoke(arguments);
        }

        protected void InvokeAction(params object?[] arguments)
        {
            endpoint.Calls++;
            if (!HasAction) throw new MissingMethodException("Action IPC is unavailable");
            endpoint.Action!.DynamicInvoke(arguments);
        }
    }

    private sealed class Gate0<TRet>(FakePlugin plugin, Endpoint endpoint) : GateBase(plugin, endpoint), ICallGateSubscriber<TRet>
    {
        public TRet InvokeFunc() => (TRet)InvokeFunction()!;
        public void InvokeAction() => base.InvokeAction([]);
    }

    private sealed class Gate2<T1, T2, TRet>(FakePlugin plugin, Endpoint endpoint) : GateBase(plugin, endpoint), ICallGateSubscriber<T1, T2, TRet>
    {
        public void InvokeAction(T1 first, T2 second) => base.InvokeAction(first, second);
    }

    private sealed class Gate3<T1, T2, T3, TRet>(FakePlugin plugin, Endpoint endpoint) : GateBase(plugin, endpoint), ICallGateSubscriber<T1, T2, T3, TRet>
    {
        public TRet InvokeFunc(T1 first, T2 second, T3 third) => (TRet)InvokeFunction(first, second, third)!;
    }

    private sealed class Gate4<T1, T2, T3, T4, TRet>(FakePlugin plugin, Endpoint endpoint) : GateBase(plugin, endpoint), ICallGateSubscriber<T1, T2, T3, T4, TRet>
    {
        public TRet InvokeFunc(T1 first, T2 second, T3 third, T4 fourth) => (TRet)InvokeFunction(first, second, third, fourth)!;
    }
}

internal sealed class FakeFramework : IFramework
{
    private Action<IFramework>? _handlers;
    public int SubscriberCount => _handlers?.GetInvocationList().Length ?? 0;
    public event Action<IFramework> Update
    {
        add => _handlers += value;
        remove => _handlers -= value;
    }
    public void Tick() => _handlers?.Invoke(this);
}

internal sealed class FakeClient : IClientState
{
    public bool IsLoggedIn { get; set; } = true;
    public uint TerritoryType { get; set; } = 10;
    public uint MapId { get; set; } = 20;
}

internal sealed class FakePlayerState : IPlayerState
{
    public bool IsLoaded { get; set; } = true;
    public WorldReference CurrentWorld { get; set; } = new(100);
}

internal sealed class FakeObjects : IObjectTable
{
    public PlayerObject? LocalPlayer { get; set; } = new() { Position = new(1, 2, 3) };
}

internal sealed class FakeData : IDataManager
{
    public ExcelSheet<Map> Maps { get; } = new() { Row = new Map(100) };
    public ExcelSheet<T> GetExcelSheet<T>() where T : struct =>
        typeof(T) == typeof(Map) ? (ExcelSheet<T>)(object)Maps : new();
}

internal sealed class Fixture : IDisposable
{
    public FakePlugin Plugin { get; } = new();
    public FakeFramework Framework { get; } = new();
    public FakeClient Client { get; } = new();
    public FakePlayerState PlayerState { get; } = new();
    public FakeObjects Objects { get; } = new();
    public FakeData Data { get; } = new();
    public HuntTrainMob Mob { get; } = new();
    public NavigationManager Manager { get; }
    public TaskCompletionSource<List<Vector3>> NextPath { get; set; } = NewPath();
    public bool Ready { get; set; } = true;
    public bool Busy { get; set; }
    public bool Running { get; set; }
    public bool MovementAllowed { get; set; } = true;
    public Vector3? Floor { get; set; } = new Vector3(250, 17, 150);
    public int PathfindRequests { get; private set; }
    public int MoveRequests { get; private set; }
    public int StopRequests { get; private set; }
    public int FloorQueries { get; private set; }
    public Vector3 LastFrom { get; private set; }
    public Vector3 LastDestination { get; private set; }
    public bool LastFly { get; private set; }
    public CancellationToken LastCancellation { get; private set; }
    public List<Vector3>? LastPath { get; private set; }
    public Vector3 LastFloorProbe { get; private set; }
    public bool LastAllowUnlandable { get; private set; }
    public float LastFloorRadius { get; private set; }

    public Fixture()
    {
        MapHelpers.CurrentInstance = 1;
        Plugin.At("vnavmesh.Nav.IsReady").Function = (Func<bool>)(() => Ready);
        Plugin.At("vnavmesh.Nav.PathfindInProgress").Function = (Func<bool>)(() => Busy);
        Plugin.At("vnavmesh.Path.IsRunning").Function = (Func<bool>)(() => Running);
        Plugin.At("vnavmesh.Path.GetMovementAllowed").Function = (Func<bool>)(() => MovementAllowed);
        Plugin.At("vnavmesh.Nav.PathfindCancelable").Function =
            (Func<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>)((from, destination, fly, cancellation) =>
            {
                PathfindRequests++;
                LastFrom = from;
                LastDestination = destination;
                LastFly = fly;
                LastCancellation = cancellation;
                // Deliberately ignore cancellation. A late successful provider task
                // must still be harmless after Stop, a location change or Dispose.
                return NextPath.Task;
            });
        Plugin.At("vnavmesh.Path.MoveTo").Action = (Action<List<Vector3>, bool>)((path, fly) =>
        {
            MoveRequests++;
            LastPath = path.ToList();
            LastFly = fly;
            Running = true;
        });
        Plugin.At("vnavmesh.Path.Stop").Action = (Action)(() => { StopRequests++; Running = false; });
        Plugin.At("vnavmesh.Query.Mesh.PointOnFloor").Function = (Func<Vector3, bool, float, Vector3?>)((probe, allowUnlandable, radius) =>
        {
            FloorQueries++;
            LastFloorProbe = probe;
            LastAllowUnlandable = allowUnlandable;
            LastFloorRadius = radius;
            return Floor;
        });
        Manager = new NavigationManager(Plugin, Client, PlayerState, Objects, Data, Framework);
    }

    public static TaskCompletionSource<List<Vector3>> NewPath() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    public List<Vector3> ValidPath() => [Objects.LocalPlayer!.Position, Mob.WorldPosition ?? Floor!.Value];
    public void Complete() => NextPath.SetResult(ValidPath());
    public void Dispose() => Manager.Dispose();
}
