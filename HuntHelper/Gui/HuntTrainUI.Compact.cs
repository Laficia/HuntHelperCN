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
    private bool _applyCompactWindowBounds;
    private string CompactWindowTitle => _isS ? "S怪记录##SRankWindow" : $"{GuiResources.HuntTrainGuiText["MainWindowTitle"]}##Window";

    public void DrawHuntTrainWindow()
    {
        if (!_huntTrainWindowVisible)
        {
            _compactColumn.Cancel();
            _compactLocationColumn.Cancel();
            _compactToolbar.Cancel();
            if (_nativePanel != null) DrawNativeDialogs();
            SaveCompactWindowBounds();
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
        var drawWindow = HuntTrainWindowLayout.Begin(CompactWindowTitle, ref _huntTrainWindowVisible,
            _huntTrainWindowSize, _huntTrainWindowPos, scale, _applyCompactWindowBounds);
        _applyCompactWindowBounds = false;
        if (drawWindow)
        {
            _huntTrainWindowSize = ImGui.GetWindowSize(); _huntTrainWindowPos = ImGui.GetWindowPos();
            DrawCompactHeader(scale);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 5) * scale);
            DrawWorldTabs();
            DrawVersionTabs();
            ImGui.PopStyleVar();
            ImGui.Separator();
            DrawCompactRows(scale);
            DrawCompactToolbar(scale);
            if (_trainManager.RecordTrain) ImGui.TextColored(TrainTheme.Color(ImGuiCol.CheckMark), "● 正在录制");
            else ImGui.TextDisabled("○ 录制关闭");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"当前区服：{_trainManager.GetCurrentWorld().WorldName}\n已录入 {VisibleMobs.Count} 条\n{_relayStatus?.Invoke()}\n{_navigation.LastMessage}\n{_macros?.Status}");
            var mode = _config.HuntFinderRouteMode == Managers.Automation.PatrolRouteMode.DRCoverage ? "DR 巡航" : "预设点位";
            if (ImGui.GetWindowContentRegionMax().X - ImGui.GetCursorPosX() > 170 * scale)
            { ImGui.SameLine(); ImGui.SetCursorPosX(ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(mode).X); ImGui.TextDisabled(mode); }
            var message = _trainManager.ActionError ?? (_trainActionMessage.Length > 0 ? _trainActionMessage : _automation.CrystalTravelStatus);
            if (message.Length > 0) ImGui.TextWrapped(message);
            if (_trainManager.PersistenceError is { } error) ImGui.TextWrapped($"记录文件异常：{error}");
        }
        else { _compactColumn.Cancel(); _compactLocationColumn.Cancel(); _compactToolbar.Cancel(); }
        HuntTrainWindowLayout.End(); ImGui.PopStyleVar(5);
        DrawNativeDialogs();
        SaveCompactWindowBounds();
    }

    private void SaveCompactWindowBounds()
    {
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && (_huntTrainWindowSize != _savedWindowSize || _huntTrainWindowPos != _savedWindowPos))
        { SaveSettings(); _config.Save(); _savedWindowSize = _huntTrainWindowSize; _savedWindowPos = _huntTrainWindowPos; }
    }

    private void DrawCompactHeader(float scale)
    {
        const ImGuiTableFlags flags = ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.NoPadInnerX;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, Vector2.Zero);
        if (!ImGui.BeginTable("CompactTrainHeader", 2, flags))
        {
            ImGui.PopStyleVar();
            return;
        }
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var height = 28 * scale;
        ImGui.TableSetupColumn("##actions", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##settings", ImGuiTableColumnFlags.WidthFixed, height + 6 * scale + spacing);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);

        string finder = _patrolActive() ? "停止找怪" : _navigation.OwnsMovement ? "停止移动" : "找怪";
        string otherRank = _isS ? "A 怪狩猎车" : "S 怪记录";
        var neutral = new TrainTheme(TrainButtonTone.Neutral);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 5) * scale);
        float MinWidth(string label) => MathF.Ceiling(ImGui.CalcTextSize(label).X + 2 * ImGui.GetStyle().FramePadding.X);
        float finderMin = _isS ? 0 : MinWidth(finder);
        float macroMin = MinWidth("宏");
        float nativeMin = MinWidth("原生窗口");
        float otherMin = MinWidth(otherRank);
        float minimum = finderMin + macroMin + nativeMin + otherMin + (_isS ? 2 : 3) * spacing;
        float preferred = (_isS ? 228 : 316) * scale + (_isS ? 2 : 3) * spacing;
        float available = ImGui.GetContentRegionAvail().X;
        float right = ImGui.GetCursorScreenPos().X + available;
        float fraction = Math.Clamp((available - minimum) / Math.Max(1, preferred - minimum), 0, 1);
        bool first = true;
        bool Button(TrainTheme theme, string label, float min, float ideal)
        {
            float width = MathF.Floor(min + Math.Max(0, ideal * scale - min) * fraction);
            if (!first && ImGui.GetItemRectMax().X + spacing + width <= right + .5f)
                ImGui.SameLine();
            first = false;
            return theme.Button(label, new Vector2(width, height));
        }

        var world = _trainManager.SelectedWorldId;
        if (!_isS && Button(new TrainTheme(TrainButtonTone.Accent), finder, finderMin, 88))
            NativeDispatch(new(NativeTrainAction.Finder, world));
        if (Button(neutral, "宏", macroMin, 54)) _openMacros?.Invoke();
        if (Button(neutral, "原生窗口", nativeMin, 88))
            _config.SetNativeHuntWindows(true);
        if (Button(neutral, otherRank, otherMin, 86)) _openOther?.Invoke();
        ImGui.PopStyleVar();

        ImGui.TableSetColumnIndex(1);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);
        ImGui.PushFont(UiBuilder.IconFont);
        var settingsClicked = neutral.Button(FontAwesomeIcon.Cog.ToIconString() + "###CompactTrainSettings", new Vector2(height + 6 * scale, height));
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("设置");
        if (settingsClicked) NativeDispatch(new(NativeTrainAction.Settings, world));
        ImGui.EndTable();
        ImGui.PopStyleVar();
    }

    private void DrawCompactRows(float scale)
    {
        var visible = VisibleIndices; var mobs = visible.Select(i => _mobList[i]).ToList();
        var missing = _settings.HuntTrainShowMissingRoster && _rosterSnapshot != null ? _rosterSnapshot.Missing : Array.Empty<HuntRosterSlot>();
        bool showStatus = _trainManager.ActionError != null || _trainActionMessage.Length > 0 || _automation.CrystalTravelStatus.Length > 0 || _trainManager.PersistenceError != null;
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
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(3, 3) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(4, 5) * scale);
        if (!CompactTrainTable.Begin(height, columnWidth, locationWidth, scale, visible.Count + missing.Count))
        {
            _compactColumn.Cancel();
            _compactLocationColumn.Cancel();
            ImGui.PopStyleVar(2);
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
        var rowAction = new TrainTheme(TrainButtonTone.Row);
        var text = TrainTheme.Color(ImGuiCol.Text);
        var mutedText = TrainTheme.Color(ImGuiCol.TextDisabled);
        var locationText = Vector4.Lerp(text, mutedText, .33f);
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
            ImGui.PushStyleColor(ImGuiCol.Text, mob.Dead ? mutedText : text);
            if (_rowDrag.Selectable(mob, mob.Name + " " + mob.Instance.GetInstanceGlyph(), _selectedIndex == index))
                SelectRecordAndOpenMap(index);
            ImGui.PopStyleColor();
            if (!_rowDrag.Dragging && ImGui.IsItemHovered())
                ImGui.SetTooltip(MapHelpers.GetMapName(mob.TerritoryID) + "\n" + HuntRecordInfo.Describe(mob) + "\n单击查看地图，拖动排序，右键查看更多操作");
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
                ImGui.PushStyleColor(ImGuiCol.Text, mob.Dead ? mutedText : locationText);
                if (ImGui.Selectable(map + "##location")) _trainManager.OpenMap(mob, true);
                ImGui.PopStyleColor();
                openColumns |= ImGui.IsItemClicked(ImGuiMouseButton.Right);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{map}\n点击查看地图，右键可隐藏地点列");
            }
            ImGui.TableSetColumnIndex(actionColumn);
            var teleportProblem = _automation.GetHuntTeleportProblem(mob);
            if (DrawCompactAction(rowAction, "传送", buttonSize, teleportProblem.Length == 0, TeleportTooltip(mob, teleportProblem)))
                TeleportRecord(mob, false);
            ImGui.SameLine();
            var reason = GetMoveProblem(mob);
            if (DrawCompactAction(rowAction, "移动", buttonSize, reason.Length == 0, reason.Length == 0 ? "使用 vnavmesh 前往该点位" : reason))
                _navigation.TryMoveTo(mob);
            ImGui.SameLine();
            if (DrawCompactAction(rowAction, "坐标", buttonSize, !mob.Dead, "发送点位预设宏")) _macros?.Send(mob, _rankLabel);
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
        ImGui.PopStyleVar(2);
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
        var order = _settings.HuntTrainToolbarOrder ??= new();
        var clicked = _compactToolbar.Draw(order, scale, DrawCompactToolIcon, out var reordered);
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
        var theme = new TrainTheme(TrainButtonTone.Accent);
        ImGui.PushFont(UiBuilder.IconFont);
        var clicked = theme.Button($"{icon.ToIconString()}###CompactTrainTool{(uint)action}", size);
        ImGui.PopFont();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) ImGuiUtil.ImGui_HoveredToolTip(description);
        return clicked;
    }

    private static bool DrawCompactAction(TrainTheme theme, string label, Vector2 size, bool enabled, string hint)
    {
        ImGui.BeginDisabled(!enabled);
        var clicked = theme.Button(label, size);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(hint);
        return clicked;
    }
}
