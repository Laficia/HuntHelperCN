using Dalamud.Bindings.ImGui;
using HuntHelper.Gui.Native;
using HuntHelper.Gui.Resource;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

public partial class HuntTrainUI
{
    internal bool NativePresentation { get; set; }
    private string? _nativePanel;
    private string? _nativePopup;
    private bool _focusTrainSettings;
    private List<HuntTrainMob>? _nativeClearRecords;
    private uint _nativeClearWorld;
    private string _nativeClearLabel = "";
    private string _nativeClearToken = "";
    private bool _nativeClearAll;
    private readonly HashSet<uint> _nativeVisitedWorlds = new();
    private static string NativeKey(HuntTrainMob mob) => $"{mob.MobID}/{mob.Instance}";

    internal NativeTrainSnapshot NativeSnapshot()
    {
        var current = _trainManager.GetCurrentWorld();
        var worlds = _trainManager.GetWorlds();
        EnsureVisibleWorld(worlds, current.WorldID);
        _settings.HuntTrainWorldTabOrder ??= new();
        var worldOrder = _settings.HuntTrainWorldTabOrder;
        if (WorldTabOrder.Synchronize(worldOrder, worlds.Select(w => w.WorldID))) _config.Save();
        var world = _trainManager.SelectedWorldId;
        // Empty expansions never produce tabs in the native train. Optional placeholders
        // only fill missing hunts inside an expansion that already has real records.
        _versions.ShowEmptyRosters = false;
        var groups = _versions.Groups(_mobList);
        var orderChanged = false;
        if (!_settings.NativeVersionOrderInitialized)
        {
            foreach (var savedOrder in _settings.HuntTrainVersionTabOrders.Values)
                if (savedOrder.Remove(uint.MaxValue)) { savedOrder.Insert(0, uint.MaxValue); orderChanged = true; }
            _settings.NativeVersionOrderInitialized = true; orderChanged = true;
        }
        if (!_settings.HuntTrainVersionTabOrders.TryGetValue(world, out var versionOrder))
            _settings.HuntTrainVersionTabOrders[world] = versionOrder = new();
        var hadAll = versionOrder.Contains(uint.MaxValue);
        static uint VersionKey(int? version) => version.HasValue ? (uint)version.Value : uint.MaxValue;
        orderChanged |= WorldTabOrder.Synchronize(versionOrder, groups.Select(g => VersionKey(g.Version)));
        if (!hadAll && versionOrder.Remove(uint.MaxValue)) { versionOrder.Insert(0, uint.MaxValue); orderChanged = true; }
        if (orderChanged) _config.Save();
        if (_nativeVisitedWorlds.Add(world)) _versions.Select(world, null);
        var version = _versions.Selected(world, _mobList);
        ClampSelection();
        var nativeVisible = _huntTrainWindowVisible && NativePresentation;
        var rows = (nativeVisible ? VisibleIndices : Enumerable.Empty<int>()).Select(index =>
        {
            var mob = _mobList[index];
            var reason = GetMoveProblem(mob);
            var teleportProblem = _automation.GetHuntTeleportProblem(mob);
            return new NativeTrainRow(NativeKey(mob), mob.TerritoryID, mob.Instance, MapHelpers.GetMapName(mob.TerritoryID),
                mob.Name, false, mob.Dead, index == _selectedIndex, teleportProblem.Length == 0, reason.Length == 0,
                reason, $"{mob.WorldName} · {MapHelpers.GetMapName(mob.TerritoryID)} ({mob.Position.X:0.0}, {mob.Position.Y:0.0})\n{HuntRecordInfo.Describe(mob)}\n单击查看地图，拖动怪物名称调整顺序",
                TeleportTooltip(mob, teleportProblem));
        }).ToList();
        if (rows.Count > 0 && _settings.HuntTrainShowMissingRoster && version is >= 2)
        {
            var roster = _roster.Snapshot(world, version.Value, _mobList, territory => _instances.Instances(world, territory, _mobList));
            rows.AddRange(roster.Missing.Select(slot => new NativeTrainRow("", slot.TerritoryId, slot.Instance,
                slot.TerritoryId == 0 ? "地图待识别" : MapHelpers.GetMapName(slot.TerritoryId), slot.Entry.Name,
                true, false, false, false, false, "尚未发现", "该服务器、该分线尚无此怪记录")));
        }
        var status = new[] { _relayStatus?.Invoke(), _macros?.Status, _navigation.LastMessage, _automation.CrystalTravelStatus, _trainActionMessage, _trainManager.ActionError }
            .LastOrDefault(message => !string.IsNullOrWhiteSpace(message)) ?? "";
        return new(nativeVisible, _rankLabel, world, current.WorldName, _trainManager.RecordTrain,
            _patrolActive(), _navigation.OwnsMovement, _settings.NativeTrainGroupByMap, status,
            worldOrder.Select(id => worlds.First(w => w.WorldID == id)).Select(w => new NativeTrainTab(w.WorldID, $"{w.WorldName} ({w.Count})", w.WorldID == world)).ToArray(),
            versionOrder.Select(id => groups.First(g => VersionKey(g.Version) == id)).Select(g => new NativeTrainTab(VersionKey(g.Version), g.DisplayLabel, g.Version == version)).ToArray(), rows,
            _nativeClearRecords == null ? null : new NativeClearSnapshot(_nativeClearToken, _rankLabel, _nativeClearLabel,
                _nativeClearRecords.Count, _nativeClearRecords.Select(m => m.WorldID).Distinct().Count(), _nativeClearAll));
    }

    internal void NativeDispatch(NativeTrainRequest request)
    {
        if (request.Action == NativeTrainAction.Close) { CancelNativeClear(); _huntTrainWindowVisible = false; return; }
        if (request.Action == NativeTrainAction.Legacy)
        {
            CancelNativeClear();
            _config.SetNativeHuntWindows(false);
            return;
        }
        if (request.Action is NativeTrainAction.ConfirmClear or NativeTrainAction.CancelClear)
        {
            if (_nativeClearRecords == null || request.Key != _nativeClearToken) return;
            if (request.Action == NativeTrainAction.ConfirmClear) ConfirmNativeClear(); else CancelNativeClear();
            return;
        }
        if (request.Action == NativeTrainAction.CurrentWorld) { SelectDisplayedWorld(_trainManager.CurrentWorldId); return; }
        if (request.Action == NativeTrainAction.World) { SelectDisplayedWorld(request.World); return; }
        // Discard stale row actions if the player changes tabs before a native click is processed.
        if (request.World != _trainManager.SelectedWorldId) return;
        var index = _mobList.FindIndex(m => NativeKey(m) == request.Key);
        var mob = index >= 0 ? _mobList[index] : null;
        switch (request.Action)
        {
            case NativeTrainAction.Finder:
                if (_patrolActive()) _stopPatrol(); else if (_navigation.OwnsMovement) _navigation.Stop(); else _openFinder();
                break;
            case NativeTrainAction.Recording: _trainManager.RecordTrain = !_trainManager.RecordTrain; break;
            case NativeTrainAction.Macros: _openMacros?.Invoke(); break;
            case NativeTrainAction.OtherRank: _openOther?.Invoke(); break;
            case NativeTrainAction.Version:
                if (uint.TryParse(request.Key, out var key)) _versions.Select(request.World, key == uint.MaxValue ? null : (int)key);
                _trainActionMessage = ""; break;
            case NativeTrainAction.Select when mob != null:
                SelectRecordAndOpenMap(index); break;
            case NativeTrainAction.SelectOnly when mob != null: _selectedIndex = index; break;
            case NativeTrainAction.Map when mob != null: _trainManager.OpenMap(mob, true); break;
            case NativeTrainAction.Teleport when mob != null: TeleportRecord(mob, _openMap); break;
            case NativeTrainAction.Move when mob != null && GetMoveProblem(mob).Length == 0:
                _navigation.TryMoveToHunt(mob); break;
            case NativeTrainAction.Coordinates when mob != null: _selectedIndex = index; _macros?.Send(mob, _rankLabel); break;
            case NativeTrainAction.Dead when mob != null:
                _trainManager.SetDead(index, request.Value); if (mob.Dead && index == _selectedIndex) SelectNext(); break;
            case NativeTrainAction.Import:
                var code = ""; try { code = ImGui.GetClipboardText(); } catch { }
                _trainManager.Import(code); _nativePanel = "导入";
                _nativePopup = $"{GuiResources.HuntTrainGuiText["ImportWindowTitle"]}##popup"; break;
            case NativeTrainAction.Export: _exportWindow.Open(); break;
            case NativeTrainAction.Backups: _nativePanel = "备份与撤销"; break;
            case NativeTrainAction.Settings:
                _nativePanel = "设置";
                _focusTrainSettings = true;
                break;
            case NativeTrainAction.ClearDead: ApplyTrainChange(() => _trainManager.RemoveRecords(VisibleMobs.Where(m => m.Dead))); break;
            case NativeTrainAction.Revive:
                var count = _trainManager.RestoreRecords(VisibleMobs);
                _trainActionMessage = _trainManager.ActionError ?? $"已恢复 {count} 条记录为存活"; break;
            case NativeTrainAction.Clear:
                RequestNativeClear(); break;
            case NativeTrainAction.ClearAll:
                RequestNativeClear(allWorlds: true); break;
            case NativeTrainAction.ClearVersion:
                if (uint.TryParse(request.Key, out var clearVersion))
                {
                    var group = _versions.Groups(_mobList).Find(g => (g.Version.HasValue ? (uint)g.Version.Value : uint.MaxValue) == clearVersion);
                    if (group.Count > 0) ClearVersionTab(group.Version);
                }
                break;
            case NativeTrainAction.RowUp when mob != null:
            case NativeTrainAction.RowDown when mob != null:
                var indices = VisibleIndices;
                var rowPosition = indices.IndexOf(index);
                if (rowPosition < 0) break;
                var offset = rowPosition + (request.Action == NativeTrainAction.RowUp ? -1 : 1);
                if (offset >= 0 && offset < indices.Count) ApplyTrainChange(() => _trainManager.SwapTrainMobs(index, indices[offset]));
                break;
            case NativeTrainAction.RemoveRow when mob != null:
                if (!VisibleMobs.Contains(mob)) break;
                if (_navigation.IsMovingTo(mob)) _navigation.Stop();
                ApplyTrainChange(() => _trainManager.TrainRemove(mob)); break;
            case NativeTrainAction.ShiftWorld:
            case NativeTrainAction.ShiftVersion:
                if (uint.TryParse(request.Key, out var tab))
                {
                    var tabOrder = request.Action == NativeTrainAction.ShiftWorld ? _settings.HuntTrainWorldTabOrder : _settings.HuntTrainVersionTabOrders[request.World];
                    if (WorldTabOrder.MoveBy(tabOrder, tab, request.Value ? 1 : -1)) _config.Save();
                }
                break;
            case NativeTrainAction.DeleteWorld:
                if (uint.TryParse(request.Key, out var deleted))
                {
                    var stop = _trainManager.GetTrainRecords(deleted).Any(_navigation.IsMovingTo);
                    if (_trainManager.DeleteWorld(deleted))
                    {
                        if (stop) _navigation.Stop();
                        _settings.HuntTrainWorldTabOrder.Remove(deleted); _selectedByWorld.Remove(deleted); _versions.Forget(deleted);
                        EnsureVisibleWorld(_trainManager.GetWorlds(), _trainManager.CurrentWorldId); _config.Save();
                    }
                }
                break;
            case NativeTrainAction.ReorderRow when mob != null:
                var target = _mobList.FirstOrDefault(m => NativeKey(m) == request.Target);
                if (target == null || target == mob || !VisibleMobs.Contains(mob) || !VisibleMobs.Contains(target)) break;
                if (_settings.NativeTrainGroupByMap && (target.TerritoryID != mob.TerritoryID || target.Instance != mob.Instance)) break;
                var visible = VisibleMobs;
                var reordered = visible.ToList(); reordered.Remove(mob);
                reordered.Insert(reordered.IndexOf(target) + (request.Value ? 1 : 0), mob);
                ApplyTrainChange(() => _trainManager.ReorderTrainMobs(visible, reordered)); break;
            case NativeTrainAction.ReorderWorld:
            case NativeTrainAction.ReorderVersion:
                if (uint.TryParse(request.Key, out var sourceId) && uint.TryParse(request.Target, out var targetId))
                {
                    var order = request.Action == NativeTrainAction.ReorderWorld ? _settings.HuntTrainWorldTabOrder : _settings.HuntTrainVersionTabOrders[request.World];
                    if (WorldTabOrder.MoveRelative(order, sourceId, targetId, request.Value)) _config.Save();
                }
                break;
        }
    }

    private void RequestNativeClear(bool allWorlds = false)
    {
        _nativeClearWorld = _trainManager.SelectedWorldId;
        _nativeClearAll = allWorlds;
        _nativeClearToken = Guid.NewGuid().ToString("N");
        _nativeClearRecords = allWorlds ? _trainManager.GetAllTrainRecords() : VisibleMobs.ToList();
        var server = _trainManager.GetWorlds().FirstOrDefault(w => w.WorldID == _nativeClearWorld)?.WorldName ?? "当前服务器";
        _nativeClearLabel = allWorlds ? $"全部服务器 / 全部版本（{_rankLabel}怪）" : $"{server} / {VersionLabel}";
    }

    private void CancelNativeClear()
    {
        _nativeClearRecords = null; _nativeClearToken = "";
    }

    private void ConfirmNativeClear()
    {
        if (_nativeClearRecords == null) return;
        var captured = _nativeClearRecords;
        var stopNavigation = captured.Any(_navigation.IsMovingTo);
        var before = _trainManager.GetAllTrainRecords().Count;
        var previousWorld = _trainManager.SelectedWorldId;
        try
        {
            if (_nativeClearAll) _trainManager.RemoveRecordsAcrossWorlds(captured);
            else
            {
                _trainManager.SelectWorld(_nativeClearWorld);
                _trainManager.RemoveRecords(captured, pauseRecording: true);
            }
        }
        finally { _trainManager.SelectWorld(previousWorld); }
        var removed = before - _trainManager.GetAllTrainRecords().Count;
        if (_trainManager.ActionError == null && removed > 0)
        {
            if (stopNavigation) _navigation.Stop();
            if (_nativeClearAll && !_isS && _patrolActive()) _stopPatrol();
        }
        _trainActionMessage = _trainManager.ActionError ?? $"已清除 {removed} 条记录，可在备份撤销中恢复";
        CancelNativeClear(); ClampSelection();
    }

    private void DrawNativeDialogs()
    {
        if (_trainManager.ImportFromIPC)
        {
            _trainManager.ImportFromIPC = false; _nativePanel = "导入";
            _nativePopup = $"{GuiResources.HuntTrainGuiText["ImportWindowTitle"]}##popup";
        }
        if (_nativePanel == null) return;
        var open = true;
        if (_focusTrainSettings)
        {
            if (_nativePanel == "设置") ImGui.SetNextWindowFocus();
            _focusTrainSettings = false;
        }
        ImGui.SetNextWindowSize(new Vector2(420, 180), ImGuiCond.FirstUseEver);
        if (ImGui.Begin($"{(_isS ? "S 怪记录" : "狩猎车")} · {_nativePanel}###NativeTrainTools{_rankLabel}", ref open, ImGuiWindowFlags.AlwaysAutoResize))
        {
            if (_nativePopup != null) { ImGui.OpenPopup(_nativePopup); _nativePopup = null; }
            if (_nativePanel == "备份与撤销") DrawSnapshotSettings();
            if (_nativePanel == "设置") DrawTrainSettings();
            if (_nativePanel == "导入" && ImGui.Button("重新读取剪贴板"))
            { _trainManager.Import(ImGui.GetClipboardText()); ImGui.OpenPopup($"{GuiResources.HuntTrainGuiText["ImportWindowTitle"]}##popup"); }
            DrawImportWindowModal(); DrawRestoreModal();
            if (_teleportManager.TeleportPluginNotFound) ImGui.OpenPopup($"{GuiResources.HuntTrainGuiText["TeleportWindowTitle"]}##ModalPopupWindowThingymajig");
            DrawTeleportPluginNotFoundModal();
        }
        ImGui.End();
        if (!open) _nativePanel = null;
    }
}
