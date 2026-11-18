using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
    private readonly CompactTrainColumnResize _compactColumn = new();
    private readonly CompactTrainColumnResize _compactLocationColumn = new(defaultWidth: 128);
    private readonly CompactTrainToolbar _compactToolbar = new();

    public void DrawHuntTrainWindow()
    {
        if (!_huntTrainWindowVisible || _nativeLegacy)
        {
            _compactColumn.Cancel();
            _compactLocationColumn.Cancel();
            _compactToolbar.Cancel();
            if (_nativeLegacy && _huntTrainWindowVisible) DrawNativeDialogs();
            return;
        }
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
            if (ImGui.Button("宏", new Vector2(54, 28) * scale)) _openMacros?.Invoke();
            if (ImGui.GetContentRegionAvail().X >= 100 * scale) { ImGui.SameLine(); if (ImGui.Button("原生窗口", new Vector2(88, 28) * scale)) { _config.UseNativeHuntWindows = true; _config.Save(); } }
            ImGui.SameLine();
            if (ImGui.GetContentRegionAvail().X > 50 * scale) ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - 42 * scale);
            if (ImGui.Button("...", new Vector2(42, 28) * scale)) ImGui.OpenPopup("CompactTrainMore");
            if (ImGui.BeginPopup("CompactTrainMore"))
            {
                if (ImGui.MenuItem("原生窗口")) { _config.UseNativeHuntWindows = true; _config.Save(); }
                if (ImGui.MenuItem(_isS ? "A 怪狩猎车" : "S 怪记录")) _openOther?.Invoke();
                ImGui.Separator();
                if (ImGui.MenuItem("设置")) NativeDispatch(new(NativeTrainAction.Settings, _trainManager.SelectedWorldId));
                if (_navigation.OwnsMovement && ImGui.MenuItem("停止移动")) { if (_patrolActive()) _stopPatrol(); else _navigation.Stop(); }
                if (ImGui.MenuItem("完整设置")) _nativeLegacy = true;
                ImGui.EndPopup();
            }
            DrawWorldTabs(false); ImGui.Separator(); DrawVersionTabs(false); ImGui.Separator();
            DrawCompactRows(scale);
            DrawCompactToolbar(scale);
            var color = _trainManager.RecordTrain ? new Vector4(.3f, .8f, .76f, 1) : new Vector4(.57f, .66f, .75f, 1);
            ImGui.TextColored(color, _trainManager.RecordTrain ? "● 正在录制" : "○ 录制关闭");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"当前区服：{_trainManager.GetCurrentWorld().WorldName}\n已录入 {VisibleMobs.Count} 条\n{_relayStatus?.Invoke()}\n{_navigation.LastMessage}\n{_macros?.Status}");
            var mode = _config.HuntFinderRouteMode == Managers.Automation.PatrolRouteMode.DRCoverage ? "DR 巡航" : "预设点位";
            if (ImGui.GetWindowContentRegionMax().X - ImGui.GetCursorPosX() > 170 * scale)
            { ImGui.SameLine(); ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(mode).X); ImGui.TextDisabled(mode); }
            var message = _trainManager.ActionError ?? (_trainActionMessage.Length > 0 ? _trainActionMessage : _automation.CrystalTravelStatus);
            if (message.Length > 0) ImGui.TextWrapped(message);
        }
        else { _compactColumn.Cancel(); _compactLocationColumn.Cancel(); _compactToolbar.Cancel(); }
        HuntTrainWindowLayout.End(); ImGui.PopStyleColor(9); ImGui.PopStyleVar(5);
        if (!_nativeLegacy) DrawNativeDialogs();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && (_huntTrainWindowSize != _savedWindowSize || _huntTrainWindowPos != _savedWindowPos))
        { SaveSettings(); _config.Save(); _savedWindowSize = _huntTrainWindowSize; _savedWindowPos = _huntTrainWindowPos; }
    }

    private void DrawCompactRows(float scale)
    {
        var visible = VisibleIndices; var mobs = visible.Select(i => _mobList[i]).ToList();
        var missing = _settings.HuntTrainShowMissingRoster && _rosterSnapshot != null ? _rosterSnapshot.Missing : Array.Empty<HuntRosterSlot>();
        bool showStatus = _trainManager.ActionError != null || _trainActionMessage.Length > 0 || _automation.CrystalTravelStatus.Length > 0;
        float footerHeight = CompactTrainToolbar.Height(scale) + (showStatus ? 60 : 38) * scale;
        float height = Math.Max(42 * scale, ImGui.GetContentRegionAvail().Y - footerHeight);
        if (visible.Count == 0 && missing.Count == 0)
        {
            _rowDrag.Cancel();
            _compactColumn.Cancel();
            _compactLocationColumn.Cancel();
            ImGui.TextDisabled("暂无记录");
            ImGui.Dummy(new(0, Math.Max(0, ImGui.GetContentRegionAvail().Y - footerHeight)));
            return;
        }
        _rowDrag.Begin($"compact/{_trainManager.SelectedWorldId}/{VersionLabel}", mobs);
        float columnWidth = _compactColumn.Width(_settings.CompactMonsterColumnWidth);
        bool showLocation = _settings.CompactShowLocationColumn;
        float? locationWidth = showLocation ? _compactLocationColumn.Width(_settings.CompactLocationColumnWidth) : null;
        int actionColumn = showLocation ? 4 : 2;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 3) * scale);
        if (!CompactTrainTable.Begin(height, columnWidth, locationWidth, scale, visible.Count + missing.Count))
        {
            _compactColumn.Cancel();
            _compactLocationColumn.Cancel();
            ImGui.PopStyleVar();
            return;
        }
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableSetColumnIndex(0); ImGui.TableHeader("怪物");
        var openColumns = ImGui.IsItemClicked(ImGuiMouseButton.Right);
        ImGui.TableSetColumnIndex(1);
        var savedWidth = _settings.CompactMonsterColumnWidth;
        if (_compactColumn.Draw(scale, ref savedWidth))
        {
            _settings.CompactMonsterColumnWidth = savedWidth;
            _config.Save();
        }
        if (showLocation)
        {
            ImGui.TableSetColumnIndex(2); ImGui.TableHeader("地点");
            openColumns |= ImGui.IsItemClicked(ImGuiMouseButton.Right);
            ImGui.TableSetColumnIndex(3);
            var savedLocationWidth = _settings.CompactLocationColumnWidth;
            if (_compactLocationColumn.Draw(scale, ref savedLocationWidth, "地点"))
            {
                _settings.CompactLocationColumnWidth = savedLocationWidth;
                _config.Save();
            }
        }
        ImGui.TableSetColumnIndex(actionColumn);
        var buttonSize = CompactTrainTable.ActionButtonSize(scale);
        ImGui.TableHeader("操作");
        openColumns |= ImGui.IsItemClicked(ImGuiMouseButton.Right);
        ImGui.TableSetColumnIndex(actionColumn + 1); ImGui.TableHeader("死亡");
        openColumns |= ImGui.IsItemClicked(ImGuiMouseButton.Right);
        var rowHeight = CompactTrainTable.RowHeight(scale);
        bool changed = false;
        for (int row = 0; row < visible.Count; row++)
        {
            var index = visible[row]; var mob = _mobList[index]; ImGui.PushID(index);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight); ImGui.TableSetColumnIndex(0);
            var rowY = ImGui.GetCursorPosY();
            ImGui.SetCursorPosY(rowY + Math.Max(0, (buttonSize.Y - ImGui.GetTextLineHeight()) / 2));
            ImGui.PushStyleColor(ImGuiCol.Text, mob.Dead ? new Vector4(.42f, .48f, .53f, 1) : new Vector4(.91f, .94f, .97f, 1));
            if (_rowDrag.Selectable(mob, mob.Name + " " + mob.Instance.GetInstanceGlyph(), _selectedIndex == index))
                _selectedIndex = index;
            ImGui.PopStyleColor();
            if (!_rowDrag.Dragging && ImGui.IsItemHovered())
                ImGui.SetTooltip(MapHelpers.GetMapName(mob.TerritoryID) + "\n" + HuntRecordInfo.Describe(mob) + "\n拖动排序，右键查看更多操作");
            if (ImGui.BeginPopupContextItem("CompactRowMenu"))
            {
                if (ImGui.MenuItem("选择")) _selectedIndex = index;
                if (ImGui.MenuItem("查看地图")) _trainManager.OpenMap(mob, true);
                if (ImGui.MenuItem("上移", enabled: row > 0)) { ApplyTrainChange(() => _trainManager.SwapTrainMobs(index, visible[row - 1])); changed = true; }
                if (ImGui.MenuItem("下移", enabled: row + 1 < visible.Count)) { ApplyTrainChange(() => _trainManager.SwapTrainMobs(index, visible[row + 1])); changed = true; }
                if (ImGui.MenuItem("删除记录")) { if (_navigation.IsMovingTo(mob)) _navigation.Stop(); ApplyTrainChange(() => _trainManager.TrainRemove(mob)); changed = true; }
                ImGui.EndPopup();
            }
            if (showLocation)
            {
                ImGui.TableSetColumnIndex(2);
                ImGui.SetCursorPosY(rowY + Math.Max(0, (buttonSize.Y - ImGui.GetTextLineHeight()) / 2));
                var map = MapHelpers.GetMapName(mob.TerritoryID);
                ImGui.PushStyleColor(ImGuiCol.Text, mob.Dead ? new Vector4(.42f, .48f, .53f, 1) : new Vector4(.66f, .75f, .85f, 1));
                if (ImGui.Selectable(map + "##location")) _trainManager.OpenMap(mob, true);
                ImGui.PopStyleColor();
                openColumns |= ImGui.IsItemClicked(ImGuiMouseButton.Right);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{map}\n点击查看地图，右键可隐藏地点列");
            }
            ImGui.TableSetColumnIndex(actionColumn);
            var teleportProblem = _automation.GetHuntTeleportProblem(mob);
            if (DrawCompactAction("传送", buttonSize, teleportProblem.Length == 0, TeleportTooltip(mob, teleportProblem)))
                TeleportRecord(mob, false);
            ImGui.SameLine();
            var reason = GetMoveProblem(mob);
            if (DrawCompactAction("移动", buttonSize, reason.Length == 0, reason.Length == 0 ? "使用 vnavmesh 前往该点位" : reason))
                _navigation.TryMoveTo(mob);
            ImGui.SameLine();
            if (DrawCompactAction("坐标", buttonSize, !mob.Dead, "发送点位预设宏")) _macros?.Send(mob, _rankLabel);
            ImGui.TableSetColumnIndex(actionColumn + 1);
            ImGui.SetCursorPosY(rowY + Math.Max(0, (buttonSize.Y - ImGui.GetFrameHeight()) / 2));
            var dead = mob.Dead;
            if (!changed && ImGui.Checkbox("##dead", ref dead)) { _trainManager.SetDead(index, dead); if (dead && index == _selectedIndex) SelectNext(); }
            ImGui.PopID(); if (changed) break;
        }
        if (!changed) foreach (var slot in missing)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight); ImGui.TableSetColumnIndex(0);
            ImGui.AlignTextToFramePadding();
            ImGui.TextColored(new Vector4(.92f, .3f, .3f, 1), slot.Entry.Name + " · 未发现");
            if (showLocation)
            {
                ImGui.TableSetColumnIndex(2);
                ImGui.AlignTextToFramePadding();
                ImGui.TextDisabled(slot.TerritoryId == 0 ? "地图待识别" : MapHelpers.GetMapName(slot.TerritoryId));
            }
        }
        var reordered = _rowDrag.Finish(scale); ImGui.EndTable();
        ImGui.PopStyleVar();
        if (CompactTrainTable.LocationToggleRequested(openColumns, showLocation))
        {
            _settings.CompactShowLocationColumn = !showLocation;
            _compactLocationColumn.Cancel();
            _config.Save();
        }
        if (changed) _rowDrag.Cancel();
        else if (reordered != null) ApplyTrainChange(() => _trainManager.ReorderTrainMobs(mobs, reordered));
    }

    private void DrawCompactToolbar(float scale)
    {
        ImGui.Separator();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.22f, .39f, .63f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.29f, .49f, .74f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.19f, .34f, .55f, 1));
        var order = _settings.HuntTrainToolbarOrder ??= new();
        var clicked = _compactToolbar.Draw(order, scale, DrawCompactToolIcon, out var reordered);
        ImGui.PopStyleColor(3);
        ImGui.Separator();
        if (reordered) _config.Save();
        if (clicked is not { } action) return;
        if (action == NativeTrainAction.Clear) ClearDisplayedRecords(VisibleMobs);
        else NativeDispatch(new(action, _trainManager.SelectedWorldId));
    }

    private bool DrawCompactToolIcon(NativeTrainAction action, Vector2 size)
    {
        var (icon, description) = action switch
        {
            NativeTrainAction.ClearDead => (FontAwesomeIcon.History, "清理死亡\n移除当前标签中已标记死亡的记录。"),
            NativeTrainAction.Revive => (FontAwesomeIcon.Syringe, "恢复存活\n清除当前标签中记录的死亡标记。"),
            NativeTrainAction.Backups => (FontAwesomeIcon.Archive, "备份与撤销\n查看操作前的备份，选择要恢复的记录。"),
            NativeTrainAction.Recording when _trainManager.RecordTrain => (FontAwesomeIcon.Pause, "停止录制\n停止扫描录入，保留已有车序。"),
            NativeTrainAction.Recording => (FontAwesomeIcon.Play, "开始录制\n将扫描到的怪物保存到当前所在服务器。"),
            NativeTrainAction.Clear => (FontAwesomeIcon.Skull, "清空当前标签\n清除当前服务器、当前标签的记录，可从备份撤销。"),
            NativeTrainAction.Export => (FontAwesomeIcon.SignOutAlt, "导出\n选择要复制到剪贴板的车序范围。"),
            _ => (FontAwesomeIcon.SignInAlt, "导入\n从剪贴板读取狩猎车序代码。"),
        };
        ImGui.PushFont(UiBuilder.IconFont);
        var clicked = ImGui.Button($"{icon.ToIconString()}###CompactTrainTool{(uint)action}", size);
        ImGui.PopFont();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) ImGuiUtil.ImGui_HoveredToolTip(description);
        return clicked;
    }

    private static bool DrawCompactAction(string label, Vector2 size, bool enabled, string hint)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, enabled ? new Vector4(.16f, .29f, .42f, 1) : new Vector4(.10f, .15f, .21f, 1));
        ImGui.BeginDisabled(!enabled);
        var clicked = ImGui.Button(label, size);
        ImGui.EndDisabled();
        ImGui.PopStyleColor();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(hint);
        return clicked;
    }
}
