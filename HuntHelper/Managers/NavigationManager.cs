using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace HuntHelper.Managers;

/// <summary>Requests ground navigation only for a hunt in the player's current world, map and instance.</summary>
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
    private readonly ICallGateSubscriber<bool> _movementAllowed;
    private readonly ICallGateSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>> _findPath;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> _move;
    private readonly ICallGateSubscriber<Vector3, bool, float, Vector3?> _pointOnFloor;
    private readonly ICallGateSubscriber<object> _stop;
    private string _availability = string.Empty;
    private long _lastStatusCheck;
    private HuntTrainMob? _movingTo;
    private Task<List<Vector3>>? _pendingPath;
    private CancellationTokenSource? _pathCancellation;
    private bool _startedMovement;
    private bool _disposed;

    public string LastMessage { get; private set; } = string.Empty;
    public bool OwnsMovement => _movingTo != null;

    public NavigationManager(IDalamudPluginInterface pluginInterface, IClientState clientState,
        IPlayerState playerState, IObjectTable objectTable, IDataManager dataManager, IFramework framework)
    {
        _clientState = clientState;
        _playerState = playerState;
        _objectTable = objectTable;
        _dataManager = dataManager;
        _framework = framework;
        _ready = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady");
        _pathfinding = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.PathfindInProgress");
        _running = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        _movementAllowed = pluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed");
        _findPath = pluginInterface.GetIpcSubscriber<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>("vnavmesh.Nav.PathfindCancelable");
        _move = pluginInterface.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
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

    private string GetContextReason(HuntTrainMob mob)
    {
        if (_disposed || !_clientState.IsLoggedIn || !_playerState.IsLoaded || _objectTable.LocalPlayer == null ||
            _clientState.TerritoryType == 0 || _clientState.MapId == 0)
            return "请先登录并等待地图加载完成";
        if (mob.Dead) return "该 A 怪已标记死亡";
        return NavigationRules.GetLocationMismatch(mob.WorldID, mob.TerritoryID, mob.MapID, mob.Instance,
            _playerState.CurrentWorld.RowId, _clientState.TerritoryType, _clientState.MapId, MapHelpers.CurrentInstance);
    }

    private void RefreshAvailability(bool force)
    {
        if (!force && Environment.TickCount64 - _lastStatusCheck < 500) return;
        _lastStatusCheck = Environment.TickCount64;
        try
        {
            if (!_ready.HasFunction || !_findPath.HasFunction || !_move.HasAction || !_stop.HasAction ||
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

            CancellationTokenSource? cancellation = null;
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
                        throw new InvalidOperationException("无法找到该坐标的可行走地面，请重新扫描这个 A 怪");
                    destination = floor.Value;
                }

                reason = GetContextReason(mob);
                if (reason.Length != 0) throw new InvalidOperationException(reason);
                var player = _objectTable.LocalPlayer ?? throw new InvalidOperationException("无法读取玩家位置");
                cancellation = new CancellationTokenSource();
                var task = _findPath.InvokeFunc(player.Position, destination, false, cancellation.Token)
                    ?? throw new InvalidOperationException("vnavmesh 未返回寻路任务");
                // A stopped/disposed request can complete later. Observe faults without
                // retaining this manager or attaching any continuation that can move.
                _ = task.ContinueWith(static completed => { _ = completed.Exception; },
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                _pendingPath = task;
                _pathCancellation = cancellation;
                cancellation = null;
                _movingTo = mob;
                _startedMovement = false;
                _lastStatusCheck = 0;
                LastMessage = $"正在寻路：{mob.Name}（{mob.WorldName}）";
                return true;
            }
            catch (Exception ex)
            {
                cancellation?.Cancel();
                cancellation?.Dispose();
                LastMessage = $"无法移动：{ex.Message}";
                PluginLog.Warning(ex, "Could not request vnavmesh pathfinding");
                return false;
            }
        }
    }

    public void Stop()
    {
        lock (_requestLock) StopCurrentRequest();
    }

    private void StopCurrentRequest()
    {
        if (_movingTo == null && _pendingPath == null) return;
        var wasMoving = _startedMovement;
        // Invalidate ownership before cancelling or invoking IPC. A late path result
        // is only a discarded task; it can never enter the movement executor.
        ClearCurrentRequest(true);
        try
        {
            if (wasMoving)
            {
                if (!_stop.HasAction) throw new InvalidOperationException("vnavmesh 停止接口不可用");
                _stop.InvokeAction();
            }
            LastMessage = wasMoving ? "已停止本插件发起的移动" : "已取消本插件发起的寻路";
        }
        catch (Exception ex)
        {
            LastMessage = $"无法确认停止：{ex.Message}";
            PluginLog.Warning(ex, "Could not stop vnavmesh movement");
        }
    }

    private void ClearCurrentRequest(bool cancel)
    {
        _pendingPath = null;
        _movingTo = null;
        _startedMovement = false;
        var cancellation = _pathCancellation;
        _pathCancellation = null;
        if (cancellation != null)
        {
            if (cancel) cancellation.Cancel();
            cancellation.Dispose();
        }
        _lastStatusCheck = 0;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        lock (_requestLock)
        {
            if (_disposed || _movingTo == null) return;
            if (GetContextReason(_movingTo).Length != 0)
            {
                StopCurrentRequest();
                return;
            }

            if (_pendingPath is { } pending)
            {
                if (!pending.IsCompleted) return;
                try
                {
                    var path = pending.GetAwaiter().GetResult();
                    if (path.Count == 0 || path.Any(p => !NavigationRules.IsFinite(p)))
                        throw new InvalidOperationException("未找到有效路线，请检查该点位或重新扫描");
                    var reason = GetContextReason(_movingTo);
                    if (reason.Length != 0) throw new InvalidOperationException(reason);
                    if (!_ready.HasFunction || !_ready.InvokeFunc() || !_move.HasAction || !_stop.HasAction)
                        throw new InvalidOperationException("vnavmesh 地图网格或移动接口已失效");
                    if (!_running.HasFunction || !_pathfinding.HasFunction || _running.InvokeFunc() || _pathfinding.InvokeFunc())
                        throw new InvalidOperationException("vnavmesh 已开始其他路线，本次寻路已取消");
                    if (_movementAllowed.HasFunction && !_movementAllowed.InvokeFunc())
                        throw new InvalidOperationException("vnavmesh 移动已暂停");

                    // This is the sole movement entry point. It runs on Framework,
                    // under the same lock as Stop/Dispose, after checking ownership.
                    _startedMovement = true;
                    _move.InvokeAction(path, false);
                    _pendingPath = null;
                    _pathCancellation?.Dispose();
                    _pathCancellation = null;
                    LastMessage = $"已开始移动：{_movingTo.Name}（{_movingTo.WorldName}）";
                }
                catch (OperationCanceledException)
                {
                    ClearCurrentRequest(false);
                    LastMessage = "寻路已取消";
                }
                catch (Exception ex)
                {
                    if (_startedMovement) StopCurrentRequest();
                    else ClearCurrentRequest(false);
                    LastMessage = $"无法移动：{ex.Message}";
                    PluginLog.Warning(ex, "Could not start vnavmesh movement");
                }
                return;
            }

            try
            {
                if (!_running.InvokeFunc())
                {
                    ClearCurrentRequest(false);
                    LastMessage = "本次移动已结束";
                }
            }
            catch (Exception) { StopCurrentRequest(); }
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
