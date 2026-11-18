using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using HuntHelper.Gui.Resource;
using HuntHelper.Managers;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace HuntHelper.Gui;

public partial class HuntTrainUI : IDisposable
{
    private readonly TrainManager _trainManager;
    private readonly Configuration _config;
    private readonly HuntTrainSettings _settings;
    private readonly string _rankLabel;
    private readonly bool _isS;
    private readonly Func<string>? _relayStatus;
    private readonly Action? _openOther;
    private readonly HuntPointMacroManager? _macros;
    private readonly Action? _openMacros;
    private readonly NavigationManager _navigation;
    private readonly Action _openFinder;
    private readonly Func<bool> _patrolActive;
    private readonly Action _stopPatrol;
    private readonly TrainVersionFilter _versions;
    private readonly HuntRoster _roster;
    private readonly HuntInstanceLayout _instances;
    private readonly Func<(uint World, uint Territory, uint Instance, bool Ready)> _location;
    private HuntRosterSnapshot? _rosterSnapshot;
    private readonly TrainVersionTabBar _versionTabs = new();
    private readonly Dictionary<uint, int> _selectedByWorld = new();
    private uint? _displayedWorld;
    private readonly WorldTabBar _worldTabs = new();
    private readonly HuntRowDragGesture<HuntTrainMob> _rowDrag = new();

    private bool _huntTrainWindowVisible = false;

    private string _copyText = GuiResources.HuntTrainGuiText["CopyText"];
    private string _trainActionMessage = string.Empty;
    private TrainSnapshot? _pendingRestore;
    private bool _openRestore;
    private Task _copyTextTask = Task.CompletedTask;
    private int _tooltipChangeTime = 400;

    #region user customisable - config
    private Vector2 _huntTrainWindowSize = new Vector2(520, 300);
    private Vector2 _huntTrainWindowPos = new Vector2(150, 150);
    private Vector2 _savedWindowSize;
    private Vector2 _savedWindowPos;

    private bool _openMap = false;
    private bool _teleportMeOnCommand = false;
    private bool _teleToAetheryte = false;
    private bool _showInChat = true;
    private bool _showTrainUIDuringIPCImport = true;
    #endregion

    private int _selectedIndex = 0;
    private List<HuntTrainMob> _mobList => _trainManager.HuntTrain;
    private List<int> VisibleIndices => _versions.VisibleIndices(_trainManager.SelectedWorldId, _mobList);
    private List<HuntTrainMob> VisibleMobs => VisibleIndices.Select(i => _mobList[i]).ToList();
    public HuntTrainMob? SelectedMacroMob => _selectedIndex >= 0 && _selectedIndex < _mobList.Count && VisibleIndices.Contains(_selectedIndex)
        ? _mobList[_selectedIndex] : null;
    private string VersionLabel => _versions.Groups(_mobList).FirstOrDefault(g => g.Version == _versions.Selected(_trainManager.SelectedWorldId, _mobList)).Label ?? "全部";
    private readonly List<HuntTrainMob> _importedTrain;

    //import new is basically the same as import all but doesn't delete anything, think it's fine to leave as default
    private bool _importAll = false;
    private bool _importNew = true;
    private bool _importUpdateTime = true;
    private IReadOnlyList<HuntTrainWorld>? _importWorldCatalog;

    private readonly TeleportManager _teleportManager = new();
    private readonly Managers.Automation.HuntAutomationManager _automation;

    public bool HuntTrainWindowVisible
    {
        get => _huntTrainWindowVisible;
        set => _huntTrainWindowVisible = value;
    }

    public HuntTrainUI(TrainManager trainManager, Configuration config, NavigationManager navigation, Managers.Automation.HuntAutomationManager automation,
        Action openFinder, Func<bool> patrolActive, Action stopPatrol, Func<HuntTrainMob, int?> aRankVersion,
        IReadOnlyList<HuntRosterEntry> roster, Func<(uint World, uint Territory, uint Instance, bool Ready)> location,
        string rankLabel = "A", Func<string>? relayStatus = null, Action? openOther = null, HuntPointMacroManager? macros = null, Action? openMacros = null)
    {
        _isS = rankLabel == "S"; _rankLabel = rankLabel;
        _settings = _isS ? (config.SRankTrain ??= new()) : config;
        _relayStatus = relayStatus; _openOther = openOther; _macros = macros; _openMacros = openMacros;
        _trainManager = trainManager;
        _trainManager.RecordInserted += OnRecordInserted;
        _navigation = navigation;
        _automation = automation;
        _openFinder = openFinder; _patrolActive = patrolActive; _stopPatrol = stopPatrol;
        _roster = new HuntRoster(roster);
        _instances = new HuntInstanceLayout(_settings.HuntTrainMapInstanceCounts ??= new());
        _location = location;
        _versions = new TrainVersionFilter(aRankVersion, _roster, rankLabel) { ShowEmptyRosters = _settings.HuntTrainShowMissingRoster };
        _settings.HuntTrainVersionTabOrders ??= new();
        _config = config;
        _importedTrain = _trainManager.ImportedTrain;
        LoadSettings();
    }

    public void Draw()
    {
        _versions.ShowEmptyRosters = _settings.HuntTrainShowMissingRoster;
        var location = _location();
        if (location.Ready) _instances.Observe(location.World, location.Territory, location.Instance);
        if (_showTrainUIDuringIPCImport && _trainManager.ImportFromIPC) { _huntTrainWindowVisible = true; }
        if (NativePresentation) { DrawNativeDialogs(); return; }
        DrawHuntTrainWindow();
    }

    public void Dispose()
    {
        _trainManager.RecordInserted -= OnRecordInserted;
        //SaveSettings();
    }

    private void OnRecordInserted(uint world, int index, int previousCount)
    {
        if (_displayedWorld == world)
            _selectedIndex = TrainVersionFilter.AfterInsertion(_selectedIndex, index, previousCount);
        else if (_selectedByWorld.TryGetValue(world, out var selected))
            _selectedByWorld[world] = TrainVersionFilter.AfterInsertion(selected, index, previousCount);
    }

    private void LoadSettings()
    {
        _huntTrainWindowSize = _settings.HuntTrainWindowSize;
        _huntTrainWindowPos = _settings.HuntTrainWindowPos;
        _savedWindowSize = _huntTrainWindowSize;
        _savedWindowPos = _huntTrainWindowPos;
        _openMap = _settings.HuntTrainNextOpensMap;
        _teleportMeOnCommand = _settings.HuntTrainNextTeleportMeOnCommand;
        _teleToAetheryte = _settings.HuntTrainTeleportToAetheryte;
        _showInChat = _settings.HuntTrainShowFlagInChat;
        _showTrainUIDuringIPCImport = _settings.HuntTrainShowUIDuringIPCImport;
    }

    public void SaveSettings()
    {
        //_settings.HuntTrainWindowVisible = HuntTrainWindowVisible;
        _settings.HuntTrainWindowSize = _huntTrainWindowSize;
        _settings.HuntTrainWindowPos = _huntTrainWindowPos;
        _settings.HuntTrainNextOpensMap = _openMap;
        _settings.HuntTrainNextTeleportMeOnCommand = _teleportMeOnCommand;
        _settings.HuntTrainTeleportToAetheryte = _teleToAetheryte;
        _settings.HuntTrainShowFlagInChat = _showInChat;
        _settings.HuntTrainShowUIDuringIPCImport = _showTrainUIDuringIPCImport;
    }


    private void DrawWorldTabs()
    {
        var current = _trainManager.GetCurrentWorld();
        if (_trainManager.SelectedWorldId != current.WorldID && current.WorldID != 0)
        {
            if (ImGui.SmallButton("查看当前服")) SelectDisplayedWorld(current.WorldID);
        }
        var worlds = _trainManager.GetWorlds();
        EnsureVisibleWorld(worlds, current.WorldID);
        _settings.HuntTrainWorldTabOrder ??= new();
        var action = _worldTabs.Draw(worlds, _trainManager.SelectedWorldId, _settings.HuntTrainWorldTabOrder, ImGuiHelpers.GlobalScale);
        if (action.Selected is { } selected) SelectDisplayedWorld(selected);
        if (action.Deleted is { } deleted)
        {
            var stop = _trainManager.GetTrainRecords(deleted).Any(_navigation.IsMovingTo);
            if (_trainManager.DeleteWorld(deleted))
            {
                if (stop) _navigation.Stop();
                _settings.HuntTrainWorldTabOrder.Remove(deleted);
                EnsureVisibleWorld(_trainManager.GetWorlds(), current.WorldID);
                _selectedByWorld.Remove(deleted);
                _versions.Forget(deleted);
            }
        }
        if (action.OrderChanged || action.Deleted.HasValue) _config.Save();
        ClampSelection();
    }

    private void EnsureVisibleWorld(IReadOnlyList<HuntTrainWorld> worlds, uint currentWorld)
    {
        if (worlds.Count == 0)
        {
            _displayedWorld = null;
            _selectedIndex = 0;
            _trainManager.SelectWorld(currentWorld);
            return;
        }
        if (_displayedWorld is { } displayed && (worlds.Any(w => w.WorldID == displayed) ||
            (displayed == currentWorld && currentWorld != 0 && _roster.Entries.Count > 0))) return;
        var selected = _trainManager.SelectedWorldId;
        if (_displayedWorld == null && currentWorld != 0 && selected == currentWorld && _roster.Entries.Count > 0)
        { SelectDisplayedWorld(currentWorld); return; }
        var next = worlds.FirstOrDefault(w => w.WorldID == selected)
            ?? worlds.FirstOrDefault(w => w.WorldID == currentWorld)
            ?? worlds.OrderBy(w => _settings.HuntTrainWorldTabOrder?.IndexOf(w.WorldID) ?? int.MaxValue).First();
        SelectDisplayedWorld(next.WorldID);
    }

    private void SelectDisplayedWorld(uint worldId)
    {
        if (_displayedWorld == worldId) return;
        if (_displayedWorld is { } previous) _selectedByWorld[previous] = _selectedIndex;
        _trainManager.SelectWorld(worldId);
        _selectedIndex = _selectedByWorld.GetValueOrDefault(worldId);
        _displayedWorld = worldId;
        _trainActionMessage = string.Empty;
    }

    private void CopyExportCode(string code)
    {
        ImGui.SetClipboardText(code);
        ChangeCopyText();
    }

    private void ClampSelection()
    {
        _selectedIndex = TrainVersionFilter.Clamp(_selectedIndex, VisibleIndices);
    }

    private void DrawVersionTabs()
    {
        var world = _trainManager.SelectedWorldId;
        if (!_settings.HuntTrainVersionTabOrders.TryGetValue(world, out var order))
            _settings.HuntTrainVersionTabOrders[world] = order = new();
        var groups = _versions.Groups(_mobList);
        var action = _versionTabs.Draw(groups, _versions.Selected(world, _mobList), ImGuiHelpers.GlobalScale, order);
        if (action.OrderChanged) _config.Save();
        if (action.Changed) { _versions.Select(world, action.Version); _trainActionMessage = string.Empty; }
        if (action.ClearVersion is { } clearVersion && groups.Find(g => g.Version == clearVersion) is { Count: > 0 } target)
            ClearVersionTab(target.Version);
        var version = _versions.Selected(world, _mobList);
        _rosterSnapshot = _settings.HuntTrainShowMissingRoster && version is >= 2
            ? _roster.Snapshot(world, version.Value, _mobList, territory => _instances.Instances(world, territory, _mobList))
            : null;
        ClampSelection();
    }

    private void ClearVersionTab(int? version) =>
        ClearDisplayedRecords(_versions.IndicesForVersion(version, _mobList).Select(i => _mobList[i]).ToList());

    private void ClearDisplayedRecords(IReadOnlyList<HuntTrainMob> records)
    {
        if (records.Count == 0) return;
        var before = _mobList.Count;
        var stopNavigation = records.Any(_navigation.IsMovingTo);
        _trainManager.RemoveRecords(records, pauseRecording: true);
        var removed = before - _mobList.Count;
        if (removed > 0 && stopNavigation) _navigation.Stop();
        _trainActionMessage = _trainManager.ActionError ?? $"已清除 {removed} 条记录，可在备份与撤销中恢复";
        ClampSelection();
    }

    private void DrawTrainSettings()
    {
        var changed = false;
        if (ImGui.CollapsingHeader("显示与窗口", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextUnformatted("原生窗口列表样式");
            if (ImGui.RadioButton("紧凑列表", !_settings.NativeTrainGroupByMap))
            { _settings.NativeTrainGroupByMap = false; changed = true; }
            ImGui.SameLine();
            if (ImGui.RadioButton("地图分组", _settings.NativeTrainGroupByMap))
            { _settings.NativeTrainGroupByMap = true; changed = true; }
            if (!NativePresentation)
            {
                var location = _settings.CompactShowLocationColumn;
                if (ImGui.Checkbox("显示地点列", ref location))
                {
                    _settings.CompactShowLocationColumn = location;
                    _compactLocationColumn.Cancel();
                    changed = true;
                }
                if (HuntTrainWindowLayout.DrawSizeControls(ImGuiHelpers.GlobalScale, ref _huntTrainWindowSize, ref _huntTrainWindowPos))
                    _applyCompactWindowBounds = true;
            }
            var missing = _settings.HuntTrainShowMissingRoster;
            if (ImGui.Checkbox("显示未发现占位（已有记录的版本）", ref missing))
            { _settings.HuntTrainShowMissingRoster = missing; changed = true; }
            changed |= ImGui.Checkbox("在本地聊天栏显示下一站坐标", ref _showInChat);
        }

        if (ImGui.CollapsingHeader("地图分线")) DrawInstanceSettings();
        if (ImGui.CollapsingHeader("录制与 Sonar"))
        {
            var capture = _settings.HuntTrainCaptureSonar;
            if (ImGui.Checkbox($"录制 Sonar 聊天中的 {_rankLabel} 怪", ref capture))
            { _settings.HuntTrainCaptureSonar = capture; changed = true; }
            ImGuiUtil.ImGui_HoveredToolTip("开启录制后，按 Sonar 消息中的区服和分线保存怪物位置。");

            if (!_isS)
            {
                var syncKills = _config.SonarSyncAKills;
                if (ImGui.Checkbox("同步 Sonar A 怪击杀标记", ref syncKills))
                { _config.SonarSyncAKills = syncKills; changed = true; }
            }
            var monitor = _isS ? _config.HuntSDeathMonitoring : _config.HuntDeathMonitoring;
            if (ImGui.Checkbox($"自动监测 {_rankLabel} 怪死亡", ref monitor))
            {
                if (_isS) _config.HuntSDeathMonitoring = monitor;
                else _config.HuntDeathMonitoring = monitor;
                changed = true;
            }
        }

        if (ImGui.CollapsingHeader("传送与插件联动"))
        {
            changed |= ImGui.Checkbox("在下一站命令中打开地图", ref _openMap);
            var next = _isS ? "/hhsn" : "/hhn";
            var crystal = _isS ? "/hhsna" : "/hhna";
            changed |= ImGui.Checkbox($"使用 {next} 时传送到最近的水晶", ref _teleportMeOnCommand);
            changed |= ImGui.Checkbox($"改为使用 {crystal} 时传送", ref _teleToAetheryte);
            if (!_isS) changed |= ImGui.Checkbox("接收其他插件的导入记录时打开狩猎车", ref _showTrainUIDuringIPCImport);
        }
        // The S window has no finder button that can double as the movement stop button.
        if (_isS && _navigation.OwnsMovement && ImGui.Button("停止移动"))
        {
            if (_patrolActive()) _stopPatrol();
            else _navigation.Stop();
        }
        if (!changed) return;
        SaveSettings();
        _config.Save();
    }

    private void DrawInstanceSettings()
    {
        var world = _trainManager.SelectedWorldId;
        var name = _trainManager.GetImportWorlds().FirstOrDefault(w => w.WorldID == world)?.WorldName ?? $"区服 {world}";
        ImGui.TextUnformatted($"当前所选服务器：{name}");
        ImGui.TextWrapped("按本服记录和到访分线自动识别。也可以手动设置每张地图的分线数量。");
        var version = _versions.Selected(world, _mobList);
        foreach (var map in _roster.Entries.Where(e => e.TerritoryId != 0 && (version == null || e.Version == version))
            .Select(e => e.TerritoryId).Distinct())
        {
            ImGui.PushID((int)map);
            ImGui.TextUnformatted(MapHelpers.GetMapName(map));
            ImGui.SameLine(); ImGui.SetNextItemWidth(145 * ImGuiHelpers.GlobalScale);
            var mode = _instances.Mode(world, map);
            static string Label(int value) => value == 0 ? "自动识别" : value == 1 ? "无分线" : $"{value} 条分线";
            if (ImGui.BeginCombo("##MapInstanceCount", Label(mode)))
            {
                for (var count = 0; count <= 9; count++)
                    if (ImGui.Selectable(Label(count), mode == count))
                    { _instances.SetMode(world, map, count); _config.Save(); }
                ImGui.EndCombo();
            }
            if (mode == 0)
            {
                var lines = _instances.Instances(world, map, _mobList);
                ImGui.SameLine(); ImGui.TextDisabled(lines[0] == 0 ? "未确认分线 / 单份名单" : $"已识别至少 {lines.Count} 线");
            }
            ImGui.PopID();
        }
    }

    private void ApplyTrainChange(Action change)
    {
        var selected = _selectedIndex >= 0 && _selectedIndex < _mobList.Count ? _mobList[_selectedIndex] : null;
        change();
        _selectedIndex = selected == null ? 0 : Math.Max(0, _mobList.IndexOf(selected));
        ClampSelection();
    }

    private bool CanTeleport(HuntTrainMob mob) => mob.WorldID != 0 && mob.WorldID == _trainManager.CurrentWorldId;

    private void SelectRecordAndOpenMap(int index)
    {
        _selectedIndex = index;
        _trainManager.OpenMap(_mobList[index], true);
    }

    private string TeleportTooltip(HuntTrainMob mob, string problem) => problem.Length != 0 ? problem :
        CanTeleport(mob) ? "传送至最近的已解锁水晶" : $"通过 DR 前往「{mob.WorldName}」，到服后传送至最近的已解锁水晶";

    private void TeleportRecord(HuntTrainMob mob, bool openMap)
    {
        var accepted = _automation.TryHuntTeleport(mob, out var error);
        _trainActionMessage = error;
        if (accepted && openMap) _trainManager.OpenMap(mob, true);
    }

    private string GetMoveProblem(HuntTrainMob mob)
    {
        if (_automation.CrystalTravelPending) return _automation.CrystalTravelStatus;
        if (_patrolActive()) return "请先停止找怪";
        return _navigation.GetDisabledReason(mob);
    }

    private void DrawTeleportPluginNotFoundModal()
    {
        var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2;
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(315 * ImGuiHelpers.GlobalScale, 120 * ImGuiHelpers.GlobalScale), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        if (ImGui.BeginPopupModal($"{GuiResources.HuntTrainGuiText["TeleportWindowTitle"]}##ModalPopupWindowThingymajig"))
        {
            ImGui.TextWrapped(GuiResources.HuntTrainGuiText["TeleportWindowMessage"]);
            ImGui.Dummy(Vector2.One);
            ImGui.SetCursorPosX(ImGui.GetWindowSize().X - 80f * ImGuiHelpers.GlobalScale);
            if (ImGui.Button(GuiResources.HuntTrainGuiText["TeleportWindowButton"]))
            {
                _teleportManager.TeleportPluginNotFound = false;
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();
        ImGui.PopStyleColor();
    }

    private void DrawImportWindowModal()
    {
        var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2;
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(400, 700), ImGuiCond.FirstUseEver);

        if (ImGui.BeginPopupModal($"{GuiResources.HuntTrainGuiText["ImportWindowTitle"]}##popup"))
        {
            //if count is zero -> show message
            if (_importedTrain.Count == 0)
            {
                ImGui.TextWrapped(GuiResources.HuntTrainGuiText["ImportWindowErrorMessage"]);
                ImGui.Dummy(new Vector2(0, 25));
                ImGui.Dummy(new Vector2(6, 0)); ImGui.SameLine();
                if (ImGui.Button(GuiResources.HuntTrainGuiText["ImportWindowCloseButton"], new Vector2(80, 0))) ImGui.CloseCurrentPopup();
                ImGui.EndPopup();
                return;
            }

            ImGui.Text($"{GuiResources.HuntTrainGuiText["ImportWindowImportedMessage"]}{_importedTrain.Count}");

            if (_trainManager.CanChooseImportWorld)
            {
                _importWorldCatalog ??= MapHelpers.GetImportableWorlds(_trainManager.CurrentWorldId);
                var destination = _importedTrain[0];
                ImGui.TextUnformatted("导入服务器");
                ImGui.SetNextItemWidth(-1);
                if (ImGui.BeginCombo("##ImportDestination", destination.WorldName, ImGuiComboFlags.HeightLarge))
                {
                    var options = _trainManager.GetImportWorlds().Concat(_importWorldCatalog)
                        .DistinctBy(w => w.WorldID).OrderBy(w => w.WorldID == 0).ThenBy(w => w.WorldName);
                    foreach (var world in options)
                    {
                        var selected = world.WorldID == destination.WorldID;
                        if (ImGui.Selectable($"{world.WorldName}##ImportWorld{world.WorldID}", selected))
                            _trainManager.SetImportWorld(world);
                        if (selected) ImGui.SetItemDefaultFocus();
                    }
                    ImGui.EndCombo();
                }
                ImGui.TextWrapped("导入到所选服务器。确认导入前可更改目标，取消不会修改现有记录。");
            }
            else ImGui.TextWrapped("分享码包含多个服务器，将按各自区服导入。");

            var previewHeight = Math.Min((ImGui.GetTextLineHeightWithSpacing() + 5) * (_importedTrain.Count + 1),
                Math.Max(80 * ImGuiHelpers.GlobalScale, ImGui.GetContentRegionAvail().Y - 200 * ImGuiHelpers.GlobalScale));
            if (ImGui.BeginChild("tablechild", new Vector2(0, previewHeight)))
            {

                if (ImGui.BeginTable("ImportTable", 2, ImGuiTableFlags.BordersH))
                {
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(GuiResources.HuntTrainGuiText["NameHeader"]);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted("信息时间");
                    foreach (var m in _importedTrain)
                    {   //green text if new mob, grey text if exists.
                        ImGui.PushStyleColor(ImGuiCol.Text,
                            !_trainManager.ContainsMob(m)
                                ? new Vector4(0.1647f, 1f, 0.647f, 1f) //greenish
                                : TrainTheme.Color(ImGuiCol.TextDisabled));
                                                                         // : new Vector4(1f, 0.345f, 0.345f, 1f)); //redish
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted($"[{m.WorldName}] {m.Name}{m.Instance.GetInstanceGlyph()}");
                        ImGui.TableNextColumn();
                        ImGui.TextUnformatted($"{(DateTime.Now.ToUniversalTime() - m.LastSeenUTC).TotalMinutes:0}分");
                        ImGui.PopStyleColor();
                    }
                    ImGui.EndTable();
                }
            }
            ImGui.EndChild();

            if (ImGui.Checkbox(GuiResources.HuntTrainGuiText["ImportAllCheckbox"], ref _importAll))
            {
                _importNew = false;
                _importUpdateTime = false;
            }
            ImGui.SameLine(); ImGuiUtil.ImGui_HelpMarker(GuiResources.HuntTrainGuiText["ImportAllCheckboxToolTip"]);
            ImGui.Dummy(new Vector2(0, 6f));
            ImGui.Separator();
            ImGui.Dummy(new Vector2(0, 6f));

            if (ImGui.Checkbox(GuiResources.HuntTrainGuiText["ImportNewCheckbox"], ref _importNew))
            {
                _importAll = false;
                if (!_importNew) _importUpdateTime = false;
            }
            ImGui.SameLine(); ImGuiUtil.ImGui_HelpMarker(GuiResources.HuntTrainGuiText["ImportNewCheckboxToolTip"]);


            ImGui.SameLine(); ImGui.Dummy(new Vector2(16, 0));
            ImGui.SameLine();

            if (ImGui.Checkbox("更新已有记录的信息时间与坐标", ref _importUpdateTime))
            {
                _importNew = true;
                _importAll = false;
            }
            ;
            ImGui.SameLine(); ImGuiUtil.ImGui_HelpMarker("合并新增记录；已有记录仅在导入信息更新时替换时间和坐标。导入接收时间与现场发现时间分别保存。");

            ImGui.Dummy(new Vector2(0, 12));
            ImGui.Dummy(new Vector2(6, 0));
            ImGui.SameLine();
            if (ImGui.Button(GuiResources.HuntTrainGuiText["ImportButton"], new Vector2(80, 0)))
            {
                if (_importNew || _importUpdateTime || _importAll)
                {
                    if (ImportTrainData())
                    {
                        _importedTrain.Clear();
                        ImGui.CloseCurrentPopup();
                    }
                }
            }
            ImGui.SameLine(); ImGui.Dummy(new Vector2(6, 0));
            ImGui.SameLine();
            if (_trainManager.ActionError != null) ImGui.TextWrapped(_trainManager.ActionError);
            if (ImGui.Button(GuiResources.HuntTrainGuiText["CancelButton"], new Vector2(80, 0)))
            {
                _importedTrain.Clear();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private bool ImportTrainData()
    {
        _selectedIndex = 0;
        if (_importAll)
        {
            return _trainManager.ImportTrainAll();
        }
        return _trainManager.ImportTrainNew(_importUpdateTime);
    }

    private void DrawSnapshotSettings()
    {
        if (!ImGui.TreeNode("备份与撤销（最近10次）")) return;
        ImGui.TextWrapped("手动删除、清空、导入、标死/恢复和调整车序前自动备份。仅含本模块怪物记录，不含路线或配置。A/S各自保存；恢复只替换涉及服务器。自动扫描不占用历史。");
        foreach (var snapshot in _trainManager.Snapshots.List())
            if (ImGui.SmallButton($"恢复 {snapshot.CreatedUtc.ToLocalTime():MM-dd HH:mm:ss} · {snapshot.Operation} · {snapshot.Worlds.Length}服 {snapshot.Records.Count}条##{snapshot.Id}"))
                RequestRestore(snapshot);
        if (_trainManager.Snapshots.Error != null) ImGui.TextWrapped(_trainManager.Snapshots.Error);
        ImGui.TreePop();
    }

    private void RequestRestore(TrainSnapshot snapshot)
    {
        _pendingRestore = snapshot;
        _openRestore = true;
    }

    private void DrawRestoreModal()
    {
        if (!HuntSnapshotRestorePopup.Begin(ref _openRestore, ImGuiHelpers.GlobalScale)) return;
        if (_pendingRestore is { } snapshot)
        {
            ImGui.TextWrapped($"恢复「{snapshot.Operation}」之前的记录？时间：{snapshot.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            var names = _trainManager.GetImportWorlds().ToDictionary(w => w.WorldID, w => w.WorldName);
            ImGui.TextWrapped("涉及服务器：" + string.Join("、", snapshot.Worlds.Select(w => names.GetValueOrDefault(w, $"区服{w}"))));
            ImGui.TextWrapped("这些服务器在此操作之后新增或修改的记录会被替换；本模块其它服和另一模块不受影响。恢复前会再备份当前状态，可再次撤销。涉及当前服时暂停该模块录制及对应巡点。");
            if (ImGui.Button("确认恢复"))
            {
                var stop = snapshot.Worlds.SelectMany(w => _trainManager.GetTrainRecords(w)).Any(_navigation.IsMovingTo);
                if (_trainManager.RestoreSnapshot(snapshot.Id))
                {
                    if (stop) _navigation.Stop();
                    ClampSelection(); _trainActionMessage = "已恢复涉及服务器的备份记录";
                    _pendingRestore = null; ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("取消")) { _pendingRestore = null; ImGui.CloseCurrentPopup(); }
            if (_trainManager.ActionError != null) ImGui.TextWrapped(_trainManager.ActionError);
        }
        ImGui.EndPopup();
    }

    //changes tooltip temporarily when clicked. ^^)b
    private void ChangeCopyText()
    {
        if (!_copyTextTask.IsCompleted) return;
        _copyTextTask = Task.Run(() =>
        {
            var temp = _copyText;
            _copyText = GuiResources.HuntTrainGuiText["CopyTextCopied"];
            Thread.Sleep(_tooltipChangeTime);
            _copyText = temp;
        });
    }

    /// <summary>
    /// called from command /hhn, sets current mob as dead, selects next, sends flag.
    /// </summary>
    public void GetNextMobCommand()
    {
        ClampSelection();
        if (VisibleIndices.Count == 0) { _trainManager.SendTrainFlag(-1, false, true); return; }
        if (_selectedIndex < _mobList.Count) _trainManager.TrainMarkAsDead(_selectedIndex);
        if (_trainManager.ActionError != null) return;
        SelectNext();
        if (!_mobList[_selectedIndex].Dead)
        {
            _trainManager.SendTrainFlag(_selectedIndex, _openMap, _showInChat);
            if (_teleportMeOnCommand && !_teleToAetheryte && CanTeleport(_mobList[_selectedIndex])) _teleportManager.TeleportToHunt(_mobList[_selectedIndex]);
        }
        else _trainManager.SendTrainFlag(-1, false, true);
    }

    /// <summary>
    /// called from command /hhna, gets flag of closest aetheryte to the next hunt mob
    /// </summary>
    public void GetNextMobNearestAetheryte()
    {
        ClampSelection();
        var nextIndex = GetNextIndex();
        if (nextIndex == -1)
        {
            _trainManager.SendTrainFlag(-1, false, true);
            return;
        }

        if (!_mobList[nextIndex].Dead)
        {
            var mob = _mobList[nextIndex];
            var aeth = _teleportManager.GetNearestAetheryte(mob.TerritoryID, mob.Position);
            if (aeth == null) return;
            _trainManager.OpenMap((AetheryteData)aeth, mob);
            if (_teleportMeOnCommand && _teleToAetheryte && CanTeleport(_mobList[nextIndex])) _teleportManager.TeleportToHunt(_mobList[nextIndex]);
        }
    }

    /// <summary>
    /// returns -1 if no living mobs remain
    /// </summary>
    /// <returns></returns>
    private int GetNextIndex()
    {
        ClampSelection();
        return TrainVersionFilter.NextLiving(_selectedIndex, VisibleIndices, _mobList);
    }

    private void SelectNext()
    {
        var nextIndex = GetNextIndex();
        _selectedIndex = nextIndex == -1 ? VisibleIndices.FirstOrDefault(0) : nextIndex;
    }

}
