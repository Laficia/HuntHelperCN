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
    private readonly Func<bool> _isFlying;
    private readonly Func<Action?>? _pendingStopResolver;
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
    public bool OwnsMovement => _movingTo != null;
    public bool IsMovingTo(HuntTrainMob mob)
    {
        lock (_requestLock) return ReferenceEquals(_movingTo, mob);
    }

    public NavigationManager(IDalamudPluginInterface pluginInterface, IClientState clientState,
        IPlayerState playerState, IObjectTable objectTable, IDataManager dataManager, IFramework framework, Func<bool>? isFlying = null, Func<Action?>? pendingStopResolver = null)
    {
        _clientState = clientState;
        _playerState = playerState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _framework = framework;
        _isFlying = isFlying ?? (() => false);
        _pendingStopResolver = pendingStopResolver;
        _nearestReachable = pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Mesh.NearestPointReachable");
        _nearestFlightPoint = pluginInterface.GetIpcSubscriber<Vector3, float, float, Vector3?>("vnavmesh.Query.Voxel.NearestPoint");
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
            else if (_movingTo != null) _availability = "本插件正在寻路或移动，请先停止当前路线";
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

    public bool TryMoveTo(HuntTrainMob mob)
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
                _requestFly = _isFlying();
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
            if (_disposed || !_nearestFlightPoint.HasFunction)
                return "DR 覆盖模式需要支持飞行体素查询接口的国服 vnavmesh，请安装并启用最新版";
            return string.Empty;
        }
    }

    /// <summary>Only queries a legal flight point. Execution remains owned by native SimpleMove.</summary>
    public bool TryResolveCoveragePoint(Vector3 candidate, out Vector3 destination)
    {
        lock (_requestLock)
        {
            destination = default;
            try
            {
                if (GetCoverageDisabledReason().Length != 0 || !_ready.HasFunction || !_ready.InvokeFunc() ||
                    !_isFlying() || !NavigationRules.IsFinite(candidate)) return false;
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
        lock (_requestLock) { if (_movingTo == null) return; Interrupted = true; StopCurrentRequest(); }
    }

    public void Stop()
    {
        lock (_requestLock) StopCurrentRequest();
    }

    private void StopCurrentRequest()
    {
        if (_movingTo == null) return;
        var cancelPending = _pendingStop;
        ClearCurrentRequest();
        try
        {
            cancelPending?.Invoke();
            if (!_stop.HasAction) throw new InvalidOperationException("vnavmesh 停止接口不可用");
            _stop.InvokeAction(); // Also stop the executor; older CN builds only do this part.
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
        _movingTo = null;
        _pendingStop = null;
        _lastStatusCheck = 0;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        lock (_requestLock)
        {
            if (_disposed || _movingTo == null) return;
            if (GetContextReason(_movingTo).Length != 0)
            {
                Interrupted = true;
                StopCurrentRequest();
                return;
            }
            try
            {
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
