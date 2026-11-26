using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using Action = System.Action;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;

namespace HuntHelper.Managers;

/// <summary>Requests CN vnavmesh navigation for a hunt in the player's current world, map and instance.</summary>
public sealed class NavigationManager : IDisposable
{
    private readonly object _requestLock = new();
    private readonly IClientState _clientState;
    private readonly IPlayerState _playerState;
    private readonly IObjectTable _objectTable;
    private readonly IDataManager _dataManager;
    private readonly IFramework _framework;
    private readonly ICallGateSubscriber<bool> _ready;
    private readonly ICallGateSubscriber<bool> _pathfinding;
    private readonly ICallGateSubscriber<bool> _running;
    private readonly ICallGateSubscriber<List<Vector3>> _remainingPath;
    private readonly ICallGateSubscriber<bool> _movementAllowed;
    private readonly ICallGateSubscriber<Vector3, bool, bool> _move;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _pointOnFloor;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> _nearestReachable;
    private readonly ICallGateSubscriber<Vector3, float, float, Vector3?> _nearestFlightPoint;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> _drFind;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _drFollow;
    private readonly ICallGateSubscriber<float, object> _drTolerance;
    private Task<List<Vector3>>? _drTask;
    private CancellationTokenSource? _drCancellation;
    private readonly Func<bool> _isFlying;
    private readonly Func<Action?>? _pendingStopResolver;
    private readonly Func<bool> _flightUnlocked;
    private readonly Func<bool> _canTakeOff;
    private readonly Action? _requestMount;
    private HuntTrainMob? _mountTarget;
    private long _mountDeadline, _nextMountRequest;
    private Action? _pendingStop;
    private bool _requestFly;
    private Vector3 _destination;
    public bool Interrupted { get; private set; }
    private readonly ICallGateSubscriber<object> _stop;
    private string _availability = string.Empty;
    private long _lastStatusCheck;
    private HuntTrainMob? _movingTo;
    private bool _disposed;

    public string LastMessage { get; private set; } = string.Empty;
    public bool OwnsMovement => _movingTo != null || _mountTarget != null;
    public bool IsMovingTo(HuntTrainMob mob)
    {
        lock (_requestLock) return ReferenceEquals(_movingTo, mob) || ReferenceEquals(_mountTarget, mob);
    }

    public NavigationManager(IDalamudPluginInterface pluginInterface, IClientState clientState,
        IPlayerState playerState, IObjectTable objectTable, IDataManager dataManager, IFramework framework,
        Func<bool>? isFlying = null, Func<Action?>? pendingStopResolver = null, Func<bool>? flightUnlocked = null,
        Func<bool>? canTakeOff = null, Action? requestMount = null)
    {
        _clientState = clientState;
        _playerState = playerState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _framework = framework;
        _isFlying = isFlying ?? (() => false);
        _pendingStopResolver = pendingStopResolver;
        _flightUnlocked = flightUnlocked ?? (() => false);
        _canTakeOff = canTakeOff ?? (() => false);
        _requestMount = requestMount;
        _nearestReachable = pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        _nearestFlightPoint = pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Voxel.NearestPoint");
        _drFind = pluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable");
        _drFollow = pluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        _drTolerance = pluginInterface.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
        _ready = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _pathfinding = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        _running = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _remainingPath = pluginInterface.GetIpcSubscriber<List<Vector3>>("vnavmesh.Path.ListWaypoints");
        _movementAllowed = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed");
        _move = pluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo");
        _pointOnFloor = pluginInterface.GetIpcSubscriber<Vector3, bool, float, Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        _stop = pluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        _framework.Update += OnFrameworkUpdate;
    }

    public string GetDisabledReason(HuntTrainMob mob)
    {
        lock (_requestLock)
        {
            var contextReason = GetContextReason(mob);
            if (contextReason.Length != 0) return contextReason;
            RefreshAvailability(false);
            return _availability;
        }
    }

    /// <summary>Reads the native executor's remaining nodes without calculating or changing a route.</summary>
    public IReadOnlyList<Vector3> GetOwnedPath(out string status)
    {
        lock (_requestLock)
        {
            status = "当前没有本插件正在执行的路线";
            if (_disposed || _movingTo == null || Interrupted) return Array.Empty<Vector3>();
            var request = _movingTo;
            try
            {
                var mismatch = GetContextReason(_movingTo);
                if (mismatch.Length != 0)
                { status = $"暂不显示路线：{mismatch}"; return Array.Empty<Vector3>(); }
                if (!_pathfinding.HasFunction || !_running.HasFunction)
                { status = "vnavmesh 当前不可用，暂不显示路线"; return Array.Empty<Vector3>(); }
                // Check pending work first: the global executor can still contain
                // another request's old nodes while native SimpleMove is calculating.
                if (_pathfinding.InvokeFunc())
                { status = "vnavmesh 正在寻路，等待执行路径"; return Array.Empty<Vector3>(); }
                if (!_running.InvokeFunc())
                { status = "当前没有正在执行的 vnavmesh 路线"; return Array.Empty<Vector3>(); }
                if (!_remainingPath.HasFunction)
                { status = "当前 vnavmesh 不支持读取执行路径"; return Array.Empty<Vector3>(); }
                var nodes = _remainingPath.InvokeFunc();
                if (nodes == null || nodes.Count == 0)
                { status = "vnavmesh 执行路径为空"; return Array.Empty<Vector3>(); }
                if (nodes.Count > 4096)
                { status = "vnavmesh 路径节点过多，暂不显示路线"; return Array.Empty<Vector3>(); }
                var snapshot = nodes.ToArray();
                foreach (var node in snapshot)
                    if (!NavigationRules.IsFinite(node))
                    { status = "vnavmesh 返回无效路径节点，暂不显示路线"; return Array.Empty<Vector3>(); }
                // Native partial paths and safe/landing-point projection can end
                // away from the requested goal. Display their actual nodes rather
                // than hiding the path or inventing a final segment to that goal.
                var endpointDistance = Vector3.Distance(snapshot[^1], _destination);
                if (_disposed || !ReferenceEquals(_movingTo, request) || Interrupted || GetContextReason(request).Length != 0)
                { status = "当前路线或所在地区已改变，暂不显示路线"; return Array.Empty<Vector3>(); }
                status = endpointDistance > 10
                    ? $"vnav 当前路径（终点距本次目标 {endpointDistance:0.0} 米）"
                    : "vnav 当前执行路径";
                return snapshot;
            }
            catch (Exception)
            {
                status = "暂时无法读取 vnavmesh 执行路径";
                return Array.Empty<Vector3>();
            }
        }
    }

    private string GetContextReason(HuntTrainMob mob)
    {
        if (_disposed || !_clientState.IsLoggedIn || !_playerState.IsLoaded || _objectTable.LocalPlayer == null ||
            _clientState.TerritoryType == 0 || _clientState.MapId == 0)
            return "请先登录并等待地图加载完成";
        if (mob.Dead) return "该 怪物已标记死亡";
        return NavigationRules.GetLocationMismatch(mob.WorldID, mob.TerritoryID, mob.MapID, mob.Instance,
            _playerState.CurrentWorld.RowId, _clientState.TerritoryType, _clientState.MapId, MapHelpers.CurrentInstance);
    }

    private void RefreshAvailability(bool force)
    {
        if (!force && Environment.TickCount64 - _lastStatusCheck < 500) return;
        _lastStatusCheck = Environment.TickCount64;
        try
        {
            if (!_ready.HasFunction || !_move.HasFunction || !_stop.HasAction ||
                !_running.HasFunction || !_pathfinding.HasFunction)
                _availability = "请先安装并启用支持导航接口的 vnavmesh";
            else if (OwnsMovement) _availability = "本插件正在准备坐骑、寻路或移动，请先停止当前路线";
            else if (!_ready.InvokeFunc()) _availability = "vnavmesh 地图网格尚未就绪";
            else if (_running.InvokeFunc() || _pathfinding.InvokeFunc()) _availability = "vnavmesh 正在寻路或移动，请先停止当前路线";
            else if (_movementAllowed.HasFunction && !_movementAllowed.InvokeFunc()) _availability = "vnavmesh 移动已暂停，请先在其设置中恢复";
            else _availability = string.Empty;
        }
        catch (Exception)
        {
            _availability = "vnavmesh 当前不可用，请确认插件已启用";
        }
    }

    public bool TryMoveToHunt(HuntTrainMob mob)
    {
        lock (_requestLock)
        {
            RefreshAvailability(true);
            var reason = GetDisabledReason(mob);
            if (reason.Length != 0)
            {
                LastMessage = reason;
                return false;
            }
            if (_isFlying() || _canTakeOff()) return TryMoveTo(mob, fly: true);
            if (!_flightUnlocked() || _requestMount == null) return TryMoveTo(mob, fly: false);

            _mountTarget = mob;
            _mountDeadline = Environment.TickCount64 + 20000;
            _nextMountRequest = 0;
            _lastStatusCheck = 0;
            Interrupted = false;
            LastMessage = "正在准备坐骑，骑乘后开始飞行";
            return true;
        }
    }

    public bool TryMoveTo(HuntTrainMob mob, bool? fly = null)
    {
        lock (_requestLock)
        {
            RefreshAvailability(true);
            var reason = GetDisabledReason(mob);
            if (reason.Length != 0)
            {
                LastMessage = reason;
                return false;
            }

            try
            {
                Vector3 destination;
                if (mob.WorldPosition is { } position && NavigationRules.IsFinite(position)) destination = position;
                else
                {
                    var map = _dataManager.GetExcelSheet<Map>().GetRowOrDefault(mob.MapID);
                    if (map == null || map.Value.SizeFactor == 0) throw new InvalidOperationException("无法读取目标地图的坐标比例");
                    if (!_pointOnFloor.HasFunction) throw new InvalidOperationException("此记录没有高度，需要启用 vnavmesh 的地面查询接口或重新扫描");
                    var mapPosition = NavigationRules.FromHuntMapCoordinates(mob.Position, map.Value.SizeFactor);
                    if (!NavigationRules.IsFinite(mapPosition)) throw new InvalidOperationException("地图坐标超出有效范围");
                    var floor = _pointOnFloor.InvokeFunc(mapPosition, false, 5f);
                    if (floor == null || !NavigationRules.IsFinite(floor.Value))
                        throw new InvalidOperationException("无法找到该坐标的可行走地面，请重新扫描这个 怪物");
                    destination = floor.Value;
                }

                reason = GetContextReason(mob);
                if (reason.Length != 0) throw new InvalidOperationException(reason);
                var player = _objectTable.LocalPlayer ?? throw new InvalidOperationException("无法读取玩家位置");
                _requestFly = fly ?? _isFlying();
                _destination = destination;
                var from = player.Position;
                if (!_requestFly && _nearestReachable.HasFunction)
                {
                    from = _nearestReachable.InvokeFunc(from, 5f, 10f) ?? from;
                    destination = _nearestReachable.InvokeFunc(destination, 5f, 10f) ?? destination;
                }
                if (!NavigationRules.IsFinite(from) || !NavigationRules.IsFinite(destination))
                    throw new InvalidOperationException("vnavmesh 返回了无效的网格坐标，请等待网格加载完成后重试");
                _pendingStop = _pendingStopResolver?.Invoke();
                if (_pendingStopResolver != null && _pendingStop == null)
                    throw new InvalidOperationException("无法确认当前 vnavmesh 的完整停止接口，请更新国服 vnavmesh 后重试");
                // Native SimpleMove owns calculation and execution. Native stop and input
                // preferences cancel that same request; HHT never restarts a late result.
                if (!_move.InvokeFunc(destination, _requestFly))
                    throw new InvalidOperationException("vnavmesh 未接受移动请求，请检查其状态和设置");
                _destination = destination;
                _movingTo = mob;
                Interrupted = false;
                _lastStatusCheck = 0;
                LastMessage = $"正在寻路：{mob.Name}（{mob.WorldName}）";
                return true;
            }
            catch (Exception ex)
            {
                LastMessage = $"无法移动：{ex.Message}";
                PluginLog.Warning(ex, "Could not request vnavmesh pathfinding");
                return false;
            }
        }
    }

    public bool TryResolvePatrolPoint(uint mapId, Vector2 coordinates, out Vector3 destination, out string error)
    {
        destination = default; error = string.Empty;
        try
        {
            var map = _dataManager.GetExcelSheet<Map>().GetRowOrDefault(mapId);
            if (map == null || map.Value.SizeFactor == 0 || !_pointOnFloor.HasFunction)
                throw new InvalidOperationException("需要地图数据及国服 vnavmesh 地面查询接口");
            var origin = NavigationRules.FromHuntMapCoordinates(coordinates, map.Value.SizeFactor);
            var point = _pointOnFloor.InvokeFunc(origin, false, 10f);
            if (point is not { } resolved || !NavigationRules.IsFinite(resolved))
                throw new InvalidOperationException("预设点无法确定高度，请等待网格加载或更新 vnavmesh 后重试");
            destination = resolved;
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    public string GetCoverageDisabledReason()
    {
        lock (_requestLock)
        {
            if (_disposed || !_nearestFlightPoint.HasFunction || !_drFind.HasFunction || !_drFollow.HasAction || !_drTolerance.HasAction)
                return "DR 巡航需要支持飞行查询、可取消寻路和路径执行接口的国服 vnavmesh";
            return string.Empty;
        }
    }

    /// <summary>Queries the voxel using DR's original horizontal and vertical extents.</summary>
    public bool TryResolveCoveragePoint(Vector3 candidate, out Vector3 destination)
    {
        lock (_requestLock)
        {
            destination = default;
            try
            {
                if (GetCoverageDisabledReason().Length != 0 || !_ready.HasFunction || !_ready.InvokeFunc() ||
                    !NavigationRules.IsFinite(candidate)) return false;
                // The CN IPC has separate horizontal and vertical half extents.
                // This matches DR's 80m candidate retry range and 2048m height range.
                var point = _nearestFlightPoint.InvokeFunc(candidate, 80f, 2048f);
                if (point is not { } resolved || !NavigationRules.IsFinite(resolved)) return false;
                destination = resolved;
                return true;
            }
            catch (Exception ex)
            {
                LastMessage = $"无法查询飞行扫描点：{ex.Message}";
                return false;
            }
        }
    }

    public void NotifyStopCommand()
    {
        lock (_requestLock) { if (!OwnsMovement) return; Interrupted = true; StopCurrentRequest(); }
    }

    public bool CoveragePathPending => _drTask is { IsCompleted: false };
    public string GetIdleProblem()
    {
        lock (_requestLock)
        {
            RefreshAvailability(true);
            return _availability;
        }
    }

    public bool CoverageNavigationReady
    {
        get
        {
            lock (_requestLock)
            {
                try { return GetCoverageDisabledReason().Length == 0 && _ready.HasFunction && _ready.InvokeFunc(); }
                catch { return false; }
            }
        }
    }
    public bool BeginCoveragePath(HuntTrainMob target)
    {
        lock (_requestLock)
        {
            var reason = GetDisabledReason(target);
            if (reason.Length != 0 || GetCoverageDisabledReason().Length != 0 || target.WorldPosition is not { } destination ||
                !NavigationRules.IsFinite(destination))
            { LastMessage = reason.Length > 0 ? reason : "DR 寻路接口或目标不可用"; return false; }
            var cancellation = new CancellationTokenSource();
            try
            {
                var from = _objectTable.LocalPlayer!.Position + new Vector3(0, 2, 0);
                var task = _drFind.InvokeFunc(from, destination, true, cancellation.Token)
                    ?? throw new InvalidOperationException("vnavmesh 没有返回寻路任务");
                _drTask = task;
                _drCancellation = cancellation;
                _pendingStop = () => { cancellation.Cancel(); cancellation.Dispose(); };
                _movingTo = target; _destination = destination; _requestFly = true; Interrupted = false;
                _lastStatusCheck = 0;
                return true;
            }
            catch (Exception ex) { cancellation.Cancel(); cancellation.Dispose(); LastMessage = "DR 寻路失败：" + ex.Message; return false; }
        }
    }
    public List<Vector3>? TakeCoveragePath()
    {
        lock (_requestLock)
        {
            if (_drTask is not { IsCompleted: true } task) return null;
            var result = task.IsCompletedSuccessfully ? task.Result?.ToList() : null;
            if (task.IsFaulted) _ = task.Exception;
            _drCancellation?.Dispose();
            ClearCurrentRequest();
            return result;
        }
    }
    public bool FollowCoveragePath(HuntTrainMob target, List<Vector3> path)
    {
        lock (_requestLock)
        {
            if (GetDisabledReason(target).Length != 0 || path.Count == 0 || path.Any(p => !NavigationRules.IsFinite(p))) return false;
            try
            {
                _drTolerance.InvokeAction(2f);
                _movingTo = target; _destination = path[^1]; _requestFly = true; Interrupted = false;
                _lastStatusCheck = 0;
                _drFollow.InvokeAction(path.ToList(), true);
                return true;
            }
            catch (Exception ex) { StopCurrentRequest(); LastMessage = "DR 路径执行失败：" + ex.Message; return false; }
        }
    }

    public void Stop()
    {
        lock (_requestLock) StopCurrentRequest();
    }

    private void StopCurrentRequest()
    {
        if (!OwnsMovement) return;
        var stopExecutor = _movingTo != null && _drTask == null;
        var cancelPending = _pendingStop;
        ClearCurrentRequest();
        try
        {
            cancelPending?.Invoke();
            if (stopExecutor)
            {
                if (!_stop.HasAction) throw new InvalidOperationException("vnavmesh 停止接口不可用");
                _stop.InvokeAction();
            }
            LastMessage = "已停止本插件发起的寻路和移动";
        }
        catch (Exception ex)
        {
            LastMessage = $"无法确认停止：{ex.Message}";
            PluginLog.Warning(ex, "Could not stop vnavmesh movement");
        }
    }

    private void ClearCurrentRequest()
    {
        if (_drTask != null)
            _ = _drTask.ContinueWith(task => { _ = task.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        _drTask = null; _drCancellation = null;
        _movingTo = null;
        _mountTarget = null;
        _pendingStop = null;
        _lastStatusCheck = 0;
    }

    private void UpdateMount(HuntTrainMob mob)
    {
        var reason = GetContextReason(mob);
        if (reason.Length != 0 || Environment.TickCount64 >= _mountDeadline)
        {
            Interrupted = true;
            ClearCurrentRequest();
            LastMessage = reason.Length != 0 ? reason : "等待坐骑超时，请确认角色可以骑乘";
            return;
        }

        if (_isFlying() || _canTakeOff())
        {
            ClearCurrentRequest();
            Interrupted = !TryMoveTo(mob, fly: true);
            return;
        }

        if (Environment.TickCount64 < _nextMountRequest) return;
        if (_pathfinding.InvokeFunc() || _running.InvokeFunc() ||
            _movementAllowed.HasFunction && !_movementAllowed.InvokeFunc()) return;
        _nextMountRequest = Environment.TickCount64 + 1000;
        _requestMount?.Invoke();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        lock (_requestLock)
        {
            if (_disposed) return;
            if (_mountTarget is { } mountTarget)
            {
                try { UpdateMount(mountTarget); }
                catch (Exception ex)
                {
                    Interrupted = true;
                    ClearCurrentRequest();
                    LastMessage = $"坐骑准备失败：{ex.Message}";
                    PluginLog.Warning(ex, "Could not prepare mount for hunt movement");
                }
                return;
            }
            if (_movingTo == null) return;
            if (GetContextReason(_movingTo).Length != 0)
            {
                Interrupted = true;
                StopCurrentRequest();
                return;
            }
            try
            {
                if (_drTask != null) return;
                if (_pathfinding.InvokeFunc() || _running.InvokeFunc()) return;
                var delta = _objectTable.LocalPlayer!.Position - _destination;
                if (!_requestFly) delta.Y = 0;
                // Completion reporting only; never override native movement preferences.
                Interrupted = delta.LengthSquared() > 25;
                ClearCurrentRequest();
                LastMessage = Interrupted ? "vnavmesh 已停止或取消移动；本插件不会自动重启路线" : "已到达目标点位";
            }
            catch (Exception)
            {
                Interrupted = true;
                StopCurrentRequest();
            }
        }
    }

    public void Dispose()
    {
        lock (_requestLock)
        {
            if (_disposed) return;
            _disposed = true;
            _framework.Update -= OnFrameworkUpdate;
            StopCurrentRequest();
        }
    }
}
