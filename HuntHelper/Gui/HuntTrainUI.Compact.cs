using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
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
    public void DrawHuntTrainWindow()
    {
        if (!_huntTrainWindowVisible) return;
        if (_nativeLegacy) { DrawNativeDialogs(); return; }
        var scale = ImGuiHelpers.GlobalScale;
        _ = NativeSnapshot(); // Shares server/version order and empty-version rules with the native presentation.
        _versions.ShowEmptyRosters = false;
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(14, 12) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 8) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 5) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 8) * scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.055f, .075f, .105f, .98f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(.075f, .11f, .16f, 1));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(.10f, .15f, .21f, 1));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.10f, .15f, .21f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.17f, .28f, .39f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.20f, .37f, .51f, 1));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(.055f, .075f, .105f, 1));
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(.13f, .23f, .33f, .6f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(.17f, .28f, .39f, .5f));
        if (HuntTrainWindowLayout.Begin(_isS ? "S怪记录##SRankWindow" : $"{GuiResources.HuntTrainGuiText["MainWindowTitle"]}##Window",
            ref _huntTrainWindowVisible, _huntTrainWindowSize, _huntTrainWindowPos, scale))
        {
            _huntTrainWindowSize = ImGui.GetWindowSize(); _huntTrainWindowPos = ImGui.GetWindowPos();
            if (!_isS)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.17f, .35f, .50f, 1));
                if (ImGui.Button(_patrolActive() ? "停止找怪" : "找怪", new Vector2(88, 28) * scale)) { if (_patrolActive()) _stopPatrol(); else _openFinder(); }
                ImGui.PopStyleColor(); ImGui.SameLine();
            }
            if (ImGui.Button(_trainManager.RecordTrain ? "停止录制" : "开始录制", new Vector2(96, 28) * scale)) _trainManager.RecordTrain = !_trainManager.RecordTrain;
            ImGui.SameLine(); if (ImGui.Button("宏", new Vector2(54, 28) * scale)) _openMacros?.Invoke();
            if (ImGui.GetContentRegionAvail().X >= 100 * scale) { ImGui.SameLine(); if (ImGui.Button("原生窗口", new Vector2(88, 28) * scale)) { _config.UseNativeHuntWindows = true; _config.Save(); } }
            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X > 50 * scale) ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - 42 * scale);
            if (ImGui.Button("...", new Vector2(42, 28) * scale)) ImGui.OpenPopup("CompactTrainMore");
            if (ImGui.BeginPopup("CompactTrainMore"))
            {
                if (ImGui.MenuItem("原生窗口")) { _config.UseNativeHuntWindows = true; _config.Save(); }
                if (ImGui.MenuItem(_isS ? "A 怪狩猎车" : "S 怪记录")) _openOther?.Invoke();
                ImGui.Separator();
                foreach (var (label, action) in new[] { ("导入", NativeTrainAction.Import), ("导出", NativeTrainAction.Export), ("备份与撤销", NativeTrainAction.Backups),
                    ("清理死亡", NativeTrainAction.ClearDead), ("恢复存活", NativeTrainAction.Revive), ("清空当前标签", NativeTrainAction.Clear), ("设置", NativeTrainAction.Settings) })
                    if (ImGui.MenuItem(label)) NativeDispatch(new(action, _trainManager.SelectedWorldId));
                if (_navigation.OwnsMovement && ImGui.MenuItem("停止移动")) { if (_patrolActive()) _stopPatrol(); else _navigation.Stop(); }
                if (ImGui.MenuItem("完整设置")) _nativeLegacy = true;
                ImGui.EndPopup();
            }
            DrawWorldTabs(false); ImGui.Separator(); DrawVersionTabs(false); ImGui.Separator();
            DrawCompactRows(scale);
            var color = _trainManager.RecordTrain ? new Vector4(.3f, .8f, .76f, 1) : new Vector4(.57f, .66f, .75f, 1);
            ImGui.TextColored(color, _trainManager.RecordTrain ? "● 正在录制" : "○ 录制关闭");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"当前区服：{_trainManager.GetCurrentWorld().WorldName}\n已录入 {VisibleMobs.Count} 条\n{_relayStatus?.Invoke()}\n{_navigation.LastMessage}\n{_macros?.Status}");
            var mode = _config.HuntFinderRouteMode == Managers.Automation.PatrolRouteMode.DRCoverage ? "DR 巡航" : "预设点位";
            if (ImGui.GetWindowContentRegionMax().X - ImGui.GetCursorPosX() > 170 * scale)
            { ImGui.SameLine(); ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(mode).X); ImGui.TextDisabled(mode); }
            if (_trainManager.ActionError != null) ImGui.TextWrapped(_trainManager.ActionError);
        }
        HuntTrainWindowLayout.End(); ImGui.PopStyleColor(9); ImGui.PopStyleVar(5);
        if (!_nativeLegacy) DrawNativeDialogs();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && (_huntTrainWindowSize != _savedWindowSize || _huntTrainWindowPos != _savedWindowPos))
        { SaveSettings(); _config.Save(); _savedWindowSize = _huntTrainWindowSize; _savedWindowPos = _huntTrainWindowPos; }
    }

    private void DrawCompactRows(float scale)
    {
        var visible = VisibleIndices; var mobs = visible.Select(i => _mobList[i]).ToList();
        var missing = _settings.HuntTrainShowMissingRoster && _rosterSnapshot != null ? _rosterSnapshot.Missing : Array.Empty<HuntRosterSlot>();
        if (visible.Count == 0 && missing.Count == 0) { _rowDrag.Cancel(); ImGui.TextDisabled("暂无记录"); ImGui.Dummy(new(0, Math.Max(20, ImGui.GetContentRegionAvail().Y - 36 * scale))); return; }
        _rowDrag.Begin($"compact/{_trainManager.SelectedWorldId}/{VersionLabel}", mobs);
        var height = Math.Max(72 * scale, ImGui.GetContentRegionAvail().Y - 38 * scale);
        if (!ImGui.BeginTable("CompactTrainRows", 3, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX,
            new(0, height), Math.Max(ImGui.GetContentRegionAvail().X, 440 * scale))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("怪物", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 162 * scale);
        ImGui.TableSetupColumn("死亡", ImGuiTableColumnFlags.WidthFixed, 34 * scale); ImGui.TableHeadersRow();
        bool changed = false;
        for (int row = 0; row < visible.Count; row++)
        {
            var index = visible[row]; var mob = _mobList[index]; ImGui.PushID(index);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, 54 * scale); ImGui.TableNextColumn();
            var rowY = ImGui.GetCursorPosY();
            ImGui.PushStyleColor(ImGuiCol.Text, mob.Dead ? new Vector4(.42f, .48f, .53f, 1) : new Vector4(.91f, .94f, .97f, 1));
            if (_rowDrag.Selectable(mob, mob.Name + " " + mob.Instance.GetInstanceGlyph(), _selectedIndex == index))
                _selectedIndex = index;
            ImGui.PopStyleColor();
            if (!_rowDrag.Dragging && ImGui.IsItemHovered()) ImGui.SetTooltip(HuntRecordInfo.Describe(mob) + "\n拖动排序；右键选择、移动或删除");
            if (ImGui.BeginPopupContextItem("CompactRowMenu"))
            {
                if (ImGui.MenuItem("选择")) _selectedIndex = index;
                if (ImGui.MenuItem("上移", enabled: row > 0)) { ApplyTrainChange(() => _trainManager.SwapTrainMobs(index, visible[row - 1])); changed = true; }
                if (ImGui.MenuItem("下移", enabled: row + 1 < visible.Count)) { ApplyTrainChange(() => _trainManager.SwapTrainMobs(index, visible[row + 1])); changed = true; }
                if (ImGui.MenuItem("删除记录")) { if (_navigation.IsMovingTo(mob)) _navigation.Stop(); ApplyTrainChange(() => _trainManager.TrainRemove(mob)); changed = true; }
                ImGui.EndPopup();
            }
            ImGui.PushStyleColor(ImGuiCol.Text, mob.Dead ? new Vector4(.36f, .42f, .47f, 1) : new Vector4(.59f, .72f, .84f, 1));
            if (ImGui.Selectable(MapHelpers.GetMapName(mob.TerritoryID) + "##map", false)) _trainManager.OpenMap(mob, true);
            ImGui.PopStyleColor();
            ImGui.TableNextColumn(); ImGui.SetCursorPosY(rowY + 8 * scale);
            ImGui.BeginDisabled(mob.Dead);
            ImGui.BeginDisabled(!CanTeleport(mob));
            if (ImGui.Button("传送", new Vector2(50, 28) * scale)) _teleportManager.TeleportToHunt(mob);
            ImGui.EndDisabled(); ImGui.SameLine();
            var reason = _navigation.GetDisabledReason(mob);
            ImGui.BeginDisabled(reason.Length > 0 || _patrolActive());
            if (ImGui.Button("移动", new Vector2(50, 28) * scale)) _navigation.TryMoveTo(mob);
            ImGui.EndDisabled(); if (reason.Length > 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(reason);
            ImGui.SameLine(); if (ImGui.Button("坐标", new Vector2(50, 28) * scale)) _macros?.Send(mob, _rankLabel);
            ImGui.EndDisabled(); ImGui.TableNextColumn(); ImGui.SetCursorPosY(rowY + 12 * scale);
            var dead = mob.Dead;
            if (!changed && ImGui.Checkbox("##dead", ref dead)) { _trainManager.SetDead(index, dead); if (dead && index == _selectedIndex) SelectNext(); }
            ImGui.PopID(); if (changed) break;
        }
        if (!changed) foreach (var slot in missing)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, 54 * scale); ImGui.TableNextColumn();
            ImGui.TextColored(new Vector4(.92f, .3f, .3f, 1), slot.Entry.Name + " · 未发现");
            ImGui.TextDisabled(MapHelpers.GetMapName(slot.TerritoryId)); ImGui.TableNextColumn(); ImGui.TextDisabled("—"); ImGui.TableNextColumn();
        }
        var reordered = _rowDrag.Finish(scale); ImGui.EndTable();
        if (changed) _rowDrag.Cancel();
        else if (reordered != null) ApplyTrainChange(() => _trainManager.ReorderTrainMobs(mobs, reordered));
    }
}
