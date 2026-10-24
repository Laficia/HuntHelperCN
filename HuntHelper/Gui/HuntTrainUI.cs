using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
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

public class HuntTrainUI : IDisposable
{
    private readonly TrainManager _trainManager;
    private readonly Configuration _config;
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

    private bool _huntTrainWindowVisible = false;

    private string _copyText = GuiResources.HuntTrainGuiText["CopyText"];
    private string _trainActionMessage = string.Empty;
    private Task _copyTextTask = Task.CompletedTask;
    private int _tooltipChangeTime = 400;

    #region user customisable - config
    private Vector2 _huntTrainWindowSize = new Vector2(520, 300);
    private Vector2 _huntTrainWindowPos = new Vector2(150, 150);
    private Vector2 _savedWindowSize;
    private Vector2 _savedWindowPos;

    private bool _showLastSeen = true;
    private bool _useBorder = false;
    private bool _openMap = false;
    private bool _teleportMe = false;
    private bool _teleportMeOnCommand = false;
    private bool _showTeleButtons = false;
    private bool _teleToAetheryte = false;
    private bool _showInChat = true;
    private bool _showTrainUIDuringIPCImport = true;
    private bool _showMapName = false;
    #endregion

    private Vector4 _deadTextColour = new Vector4(.6f, .7f, .6f, 1f);

    private int _selectedIndex = 0;
    private List<HuntTrainMob> _mobList => _trainManager.HuntTrain;
    private List<int> VisibleIndices => _versions.VisibleIndices(_trainManager.SelectedWorldId, _mobList);
    private List<HuntTrainMob> VisibleMobs => VisibleIndices.Select(i => _mobList[i]).ToList();
    private string VersionLabel => _versions.Groups(_mobList).FirstOrDefault(g => g.Version == _versions.Selected(_trainManager.SelectedWorldId, _mobList)).Label ?? "全部";
    private readonly List<HuntTrainMob> _importedTrain;

    //import new is basically the same as import all but doesn't delete anything, think it's fine to leave as default
    private bool _importAll = false;
    private bool _importNew = true;
    private bool _importUpdateTime = true;
    private IReadOnlyList<HuntTrainWorld>? _importWorldCatalog;

    private TeleportManager _teleportManager;

    public bool HuntTrainWindowVisible
    {
        get => _huntTrainWindowVisible;
        set => _huntTrainWindowVisible = value;
    }

    public HuntTrainUI(TrainManager trainManager, Configuration config, NavigationManager navigation,
        Action openFinder, Func<bool> patrolActive, Action stopPatrol, Func<HuntTrainMob, int?> aRankVersion,
        IReadOnlyList<HuntRosterEntry> roster, Func<(uint World, uint Territory, uint Instance, bool Ready)> location)
    {
        _trainManager = trainManager;
        _navigation = navigation;
        _openFinder = openFinder; _patrolActive = patrolActive; _stopPatrol = stopPatrol;
        _roster = new HuntRoster(roster);
        _instances = new HuntInstanceLayout(config.HuntTrainMapInstanceCounts ??= new());
        _location = location;
        _versions = new TrainVersionFilter(aRankVersion, _roster) { ShowEmptyRosters = config.HuntTrainShowMissingRoster };
        config.HuntTrainVersionTabOrders ??= new();
        _config = config;
        _importedTrain = _trainManager.ImportedTrain;
        LoadSettings();
        _teleportManager = new TeleportManager();
    }

    public void Draw()
    {
        _versions.ShowEmptyRosters = _config.HuntTrainShowMissingRoster;
        var location = _location();
        if (location.Ready) _instances.Observe(location.World, location.Territory, location.Instance);
        if (_showTrainUIDuringIPCImport && _trainManager.ImportFromIPC) { _huntTrainWindowVisible = true; }
        DrawHuntTrainWindow();
    }

    public void Dispose()
    {
        //SaveSettings();
    }

    private void LoadSettings()
    {
        _huntTrainWindowSize = _config.HuntTrainWindowSize;
        _huntTrainWindowPos = _config.HuntTrainWindowPos;
        _savedWindowSize = _huntTrainWindowSize;
        _savedWindowPos = _huntTrainWindowPos;
        _showLastSeen = _config.HuntTrainShowLastSeen;
        _useBorder = _config.HuntTrainUseBorder;
        _openMap = _config.HuntTrainNextOpensMap;
        _teleportMe = _config.HuntTrainNextTeleportMe;
        _teleportMeOnCommand = _config.HuntTrainNextTeleportMeOnCommand;
        _showTeleButtons = _config.HuntTrainShowTeleportButtons;
        _teleToAetheryte = _config.HuntTrainTeleportToAetheryte;
        _showInChat = _config.HuntTrainShowFlagInChat;
        _showTrainUIDuringIPCImport = _config.HuntTrainShowUIDuringIPCImport;
        _showMapName = _config.HuntTrainShowMapName;
    }

    public void SaveSettings()
    {
        //_config.HuntTrainWindowVisible = HuntTrainWindowVisible;
        _config.HuntTrainWindowSize = _huntTrainWindowSize;
        _config.HuntTrainWindowPos = _huntTrainWindowPos;
        _config.HuntTrainShowLastSeen = _showLastSeen;
        _config.HuntTrainUseBorder = _useBorder;
        _config.HuntTrainNextOpensMap = _openMap;
        _config.HuntTrainNextTeleportMe = _teleportMe;
        _config.HuntTrainNextTeleportMeOnCommand = _teleportMeOnCommand;
        _config.HuntTrainShowTeleportButtons = _showTeleButtons;
        _config.HuntTrainTeleportToAetheryte = _teleToAetheryte;
        _config.HuntTrainShowFlagInChat = _showInChat;
        _config.HuntTrainShowUIDuringIPCImport = _showTrainUIDuringIPCImport;
        _config.HuntTrainShowMapName = _showMapName;
    }


    private Vector4 bg = new Vector4(.3f, .3f, .3f, 1f);
    private float trainItemY => 23f * ImGuiHelpers.GlobalScale;
    private ImGuiWindowFlags defaultTrainFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;

    public void DrawHuntTrainWindow()
    {
        if (!HuntTrainWindowVisible) return;
        if (_trainManager.ImportFromIPC) ImGui.SetNextWindowCollapsed(false);

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, 10);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 4));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.055f, .07f, .1f, .97f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(.07f, .1f, .15f, 1));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(.1f, .17f, .26f, 1));
        ImGui.PushStyleColor(ImGuiCol.TableHeaderBg, new Vector4(.11f, .16f, .23f, 1));
        ImGui.PushStyleColor(ImGuiCol.TableRowBg, new Vector4(.08f, .105f, .15f, .65f));
        ImGui.PushStyleColor(ImGuiCol.TableRowBgAlt, new Vector4(.105f, .135f, .18f, .65f));

        if (HuntTrainWindowLayout.Begin($"{GuiResources.HuntTrainGuiText["MainWindowTitle"]}##Window",
            ref _huntTrainWindowVisible, _huntTrainWindowSize, _huntTrainWindowPos, ImGuiHelpers.GlobalScale))
        {
            _huntTrainWindowSize = ImGui.GetWindowSize();
            _huntTrainWindowPos = ImGui.GetWindowPos();


            if (ImGui.Button("找怪 / 路线录制")) _openFinder();
            if (_patrolActive()) { ImGui.SameLine(); if (ImGui.Button("停止找怪")) _stopPatrol(); }
            DrawWorldTabs();
            DrawVersionTabs();
            DrawTrainRows();
            DrawNavigationStatus();
            if (_trainManager.PersistenceError != null)
                ImGui.TextWrapped($"记录文件异常：{_trainManager.PersistenceError}");


            if (ImGui.TreeNode($"{GuiResources.HuntTrainGuiText["Settings"]}"))
            {
                var showMissingRoster = _config.HuntTrainShowMissingRoster;
                if (ImGui.Checkbox("显示未发现 A 怪占位", ref showMissingRoster))
                {
                    _config.HuntTrainShowMissingRoster = showMissingRoster;
                    _versions.ShowEmptyRosters = showMissingRoster;
                    _config.Save();
                }
                ImGuiUtil.ImGui_HoveredToolTip("默认关闭，只显示已记录的怪物，并隐藏无记录的版本标签。开启后按当前服务器各地图的分线补齐完整名单；设置自动保存。");
                DrawInstanceSettings();
                HuntTrainWindowLayout.DrawSizeControls(ImGuiHelpers.GlobalScale);
                ImGui.Separator();
                if (ImGui.TreeNode("显示设置##iwanttodeletethisbutmaybesomeusersactuallyuseit"))
                {
                    if (ImGui.BeginTable("settingsalignment", 2))
                    {
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["Border"], ref _useBorder); //doesn't do anything anymore
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["LastSeen"], ref _showLastSeen);
                        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["LastSeenToolTip"]);
                        ImGui.TableNextColumn();
                        ImGui.Checkbox("显示地图名称", ref _showMapName);
                        ImGuiUtil.ImGui_HoveredToolTip("显示地图名称");
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["ShowFlagInChat"], ref _showInChat);
                        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["ShowFlagInChat"]);
                        ImGui.EndTable();
                    }
                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("传送设置###Teleport"))
                {
                    if (ImGui.BeginTable("settingsalignment", 2))
                    {
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["OpenMap"], ref _openMap);
                        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["OpenMapToolTip"]);
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["TeleButtons"], ref _showTeleButtons);
                        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["TeleButtonsToolTip"]);
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["TeleportOnClick"], ref _teleportMe);
                        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["TeleportOnClickToolTip"]);
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["TeleportOnCommand"], ref _teleportMeOnCommand);
                        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["TeleportOnCommand"]);
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["TeleportToAetheryte"], ref _teleToAetheryte);
                        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["TeleportToAetheryte"]);
                        ImGui.EndTable();
                    }
                    ImGui.TreePop();
                }

                if (ImGui.TreeNode("插件联动###IPCRelatedStuffIdk"))
                {
                    if (ImGui.BeginTable("settingsalignment", 1))
                    {
                        ImGui.TableNextColumn();
                        ImGui.Checkbox(GuiResources.HuntTrainGuiText["OpenUIWhenIPCImport"], ref _showTrainUIDuringIPCImport);
                        ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["OpenUIWhenIPCImport"]);
                        ImGui.EndTable();
                    }
                    ImGui.TreePop();
                }

                ImGui.TreePop();
                ImGui.Separator();
            }
            ImGui.Dummy(new Vector2(0, 12f));


            #region Buttons

            if (ImGuiComponents.IconButton(FontAwesomeIcon.History)) ApplyTrainChange(() => _trainManager.RemoveRecords(VisibleMobs.Where(m => m.Dead)));
            ImGuiUtil.ImGui_HoveredToolTip($"删除当前 {VersionLabel} 标签内已死亡的记录");
            ImGui.SameLine(); ImGui.Dummy(new Vector2(4, 0)); ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Syringe))
            {
                var restored = _trainManager.RestoreRecords(VisibleMobs);
                _trainActionMessage = restored > 0 ? $"已恢复当前 {VersionLabel} 标签内 {restored} 条记录为存活"
                    : $"当前 {VersionLabel} 标签内没有已死亡的记录，无需恢复";
            }
            ImGuiUtil.ImGui_HoveredToolTip($"恢复当前 {VersionLabel} 标签内已死亡的记录为存活；只修改记录的死亡标记，不会重新扫描。");
            ImGui.SameLine(); ImGui.Dummy(new Vector2(4, 0)); ImGui.SameLine();

            //position record button on far right
            ImGui.SetCursorPosX(ImGui.GetWindowSize().X - (50 * ImGuiHelpers.GlobalScale));
            if (!_trainManager.RecordTrain)
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Play)) _trainManager.RecordTrain = true;
                ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["StartRecordingButton"]);
            }
            else
            {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.Pause)) _trainManager.RecordTrain = false;
                ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["StopRecordingButton"]);
            }

            ImGui.Dummy(new Vector2(0, 20f));

            if (ImGuiComponents.IconButton(FontAwesomeIcon.Skull)) ImGui.OpenPopup($"{GuiResources.HuntTrainGuiText["DeleteWindowTitle"]}##modal");
            ImGuiUtil.ImGui_HoveredToolTip($"清空当前 {VersionLabel} 标签内记录");
            ImGui.SameLine();

            //gosh these buttons don't line up, off by like 1 pixel :(
            ImGui.SetCursorPosX(ImGui.GetWindowSize().X - (80 * ImGuiHelpers.GlobalScale));
            if (ImGuiComponents.IconButton(FontAwesomeIcon.SignOutAlt))
            {
                ImGui.OpenPopup("HuntTrainExportScope");
            }

            ImGuiUtil.ImGui_HoveredToolTip(_copyText);
            if (ImGui.BeginPopup("HuntTrainExportScope"))
            {
                ImGui.TextUnformatted("选择导出范围");
                ImGui.Separator();
                var visible = VisibleMobs;
                if (ImGui.MenuItem($"当前标签：{VersionLabel}（{visible.Count} 条）", enabled: visible.Count > 0))
                    CopyExportCode(ExportImport.Export(visible));
                var selectedName = _trainManager.GetWorlds().FirstOrDefault(w => w.WorldID == _trainManager.SelectedWorldId)?.WorldName ?? "当前服务器";
                if (ImGui.MenuItem($"当前服务器：{selectedName}（{_mobList.Count} 条）", enabled: _mobList.Count > 0))
                    CopyExportCode(_trainManager.ExportSelectedTrain());
                var all = _trainManager.GetAllTrainRecords(_config.HuntTrainWorldTabOrder);
                if (ImGui.MenuItem($"全部服务器（{all.Select(m => m.WorldID).Distinct().Count()} 服，{all.Count} 条）", enabled: all.Count > 0))
                    CopyExportCode(_trainManager.ExportAllTrains(_config.HuntTrainWorldTabOrder));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip("定制版可按服务器恢复。原版不区分区服，分享给原版用户时建议导出单个服务器。");
                ImGui.EndPopup();
            }
            ImGui.SameLine(); ImGui.Dummy(new Vector2(4, 0)); ImGui.SameLine();

            if (ImGuiComponents.IconButton(FontAwesomeIcon.SignInAlt) || _trainManager.ImportFromIPC)
            {
                if (_trainManager.ImportFromIPC)
                {
                    _trainManager.ImportFromIPC = false;
                }
                else
                {
                    var importCode = string.Empty;
                    try
                    {
                        importCode = ImGui.GetClipboardText();
                    }
                    catch { } // if clipboard doesn't have string throws object null error. i.e. a file
                    _trainManager.Import(importCode);
                }

                ImGui.OpenPopup($"{GuiResources.HuntTrainGuiText["ImportWindowTitle"]}##popup");
            }

            ImGuiUtil.ImGui_HoveredToolTip(GuiResources.HuntTrainGuiText["ImportButton"]);

            DrawDeleteModal();
            DrawImportWindowModal();
            #endregion

            if (_teleportManager.TeleportPluginNotFound) ImGui.OpenPopup($"{GuiResources.HuntTrainGuiText["TeleportWindowTitle"]}##ModalPopupWindowThingymajig");
            DrawTeleportPluginNotFoundModal();

            _huntTrainWindowSize = ImGui.GetWindowSize();
            _huntTrainWindowPos = ImGui.GetWindowPos();

        }
        HuntTrainWindowLayout.End();
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(3); // itemspacing,  scrollbar sizing, window padding
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) &&
            (_huntTrainWindowSize != _savedWindowSize || _huntTrainWindowPos != _savedWindowPos))
        {
            SaveSettings();
            _config.Save();
            _savedWindowSize = _huntTrainWindowSize;
            _savedWindowPos = _huntTrainWindowPos;
        }
    }

    private void DrawWorldTabs()
    {
        var current = _trainManager.GetCurrentWorld();
        ImGui.TextUnformatted($"当前区服：{FormatWorld(current.WorldName, current.DataCenterName)}");
        ImGuiUtil.ImGui_HoveredToolTip("扫描保存到角色当前所在服务器。左键拖拽调整顺序；右键可选择、左右移动或删除该服记录及标签。");
        if (_trainManager.SelectedWorldId != current.WorldID && current.WorldID != 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton("查看当前服")) SelectDisplayedWorld(current.WorldID);
        }
        var worlds = _trainManager.GetWorlds();
        EnsureVisibleWorld(worlds, current.WorldID);
        _config.HuntTrainWorldTabOrder ??= new();
        var action = _worldTabs.Draw(worlds, _trainManager.SelectedWorldId, _config.HuntTrainWorldTabOrder, ImGuiHelpers.GlobalScale);
        if (action.Selected is { } selected) SelectDisplayedWorld(selected);
        if (action.Deleted is { } deleted)
        {
            if (deleted == current.WorldID) _navigation.Stop();
            _trainManager.DeleteWorld(deleted);
            _config.HuntTrainWorldTabOrder.Remove(deleted);
            EnsureVisibleWorld(_trainManager.GetWorlds(), current.WorldID);
            _selectedByWorld.Remove(deleted);
            _versions.Forget(deleted);
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
            ?? worlds.OrderBy(w => _config.HuntTrainWorldTabOrder?.IndexOf(w.WorldID) ?? int.MaxValue).First();
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

    private static string FormatWorld(string name, string dataCenter) => name;

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
        if (!_config.HuntTrainVersionTabOrders.TryGetValue(world, out var order))
            _config.HuntTrainVersionTabOrders[world] = order = new();
        var action = _versionTabs.Draw(_versions.Groups(_mobList), _versions.Selected(world, _mobList), ImGuiHelpers.GlobalScale, order);
        if (action.OrderChanged) _config.Save();
        if (action.Changed) { _versions.Select(world, action.Version); _trainActionMessage = string.Empty; }
        var version = _versions.Selected(world, _mobList);
        _rosterSnapshot = null;
        if (version is >= 2)
        {
            _rosterSnapshot = _roster.Snapshot(world, version.Value, _mobList,
                territory => _instances.Instances(world, territory, _mobList));
            var species = _roster.Progress(world, version.Value, _mobList);
            var progress = _rosterSnapshot.Progress;
            var name = _trainManager.GetImportWorlds().FirstOrDefault(w => w.WorldID == world)?.WorldName ?? $"区服 {world}";
            HuntRosterPresentation.Summary(name, version.Value, progress.Found, progress.Total, ImGuiHelpers.GlobalScale);
            ImGuiUtil.ImGui_HoveredToolTip("进度按服务器、地图和分线分别计算，死亡记录仍算已发现；不同分线不会互相填满占位。版本标签仍显示怪物种类数。占位不写入真实记录。");
            if (progress.Total != species.Total)
                ImGui.TextDisabled($"按地图分线计数 · 怪物种类 {species.Found}/{species.Total}");
            if (progress.Found < progress.Total && !_trainManager.RecordTrain && !_patrolActive())
                ImGui.TextDisabled("开启下方记录或使用找怪扫描，发现后会自动标出。");
        }
        ClampSelection();
    }

    private void DrawInstanceSettings()
    {
        if (!ImGui.TreeNode("地图分线设置（当前所选服务器）")) return;
        ImGui.TextWrapped("自动模式根据本服该地图的记录和实际到访分线识别，未确认时先显示一份。无法凭空获知尚未到访的分线，可按地图手动补全；不会自动切线。");
        var world = _trainManager.SelectedWorldId;
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
        ImGui.TreePop();
    }

    private void ApplyTrainChange(Action change)
    {
        var selected = _selectedIndex >= 0 && _selectedIndex < _mobList.Count ? _mobList[_selectedIndex] : null;
        change();
        _selectedIndex = selected == null ? 0 : Math.Max(0, _mobList.IndexOf(selected));
        ClampSelection();
    }

    private void DrawTrainRows()
    {
        var visible = VisibleIndices;
        IReadOnlyList<HuntRosterSlot> missing = _config.HuntTrainShowMissingRoster && _rosterSnapshot != null
            ? _rosterSnapshot.Missing : Array.Empty<HuntRosterSlot>();
        if (visible.Count == 0 && missing.Count == 0)
        {
            ImGui.TextWrapped("当前标签暂无记录。可在设置开启未发现占位，或开始记录／找怪扫描。");
            return;
        }
        var columns = 4 + (_showMapName ? 1 : 0) + (_showLastSeen ? 1 : 0);
        var tableHeight = Math.Clamp(ImGui.GetContentRegionAvail().Y - 140 * ImGuiHelpers.GlobalScale,
            110 * ImGuiHelpers.GlobalScale, 340 * ImGuiHelpers.GlobalScale);
        if (!ImGui.BeginTable("WorldTrainTable", columns, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH |
            ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX, new Vector2(0, tableHeight),
            Math.Max(ImGui.GetContentRegionAvail().X, 440 * ImGuiHelpers.GlobalScale))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("怪物", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.WidthFixed, 70 * ImGuiHelpers.GlobalScale);
        if (_showMapName) ImGui.TableSetupColumn("地图", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthFixed, 106 * ImGuiHelpers.GlobalScale);
        if (_showLastSeen) ImGui.TableSetupColumn("发现", ImGuiTableColumnFlags.WidthFixed, 52 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("死亡", ImGuiTableColumnFlags.WidthFixed, 36 * ImGuiHelpers.GlobalScale);
        ImGui.TableHeadersRow();
        for (var row = 0; row < visible.Count; row++)
        {
            var n = visible[row];
            var mob = _mobList[n];
            var listChanged = false;
            ImGui.PushID(n);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, mob.Dead ? _deadTextColour : Vector4.One);
            if (ImGui.Selectable($"{mob.Name} {mob.Instance.GetInstanceGlyph()}", n == _selectedIndex))
            {
                _selectedIndex = n;
                _trainManager.SendTrainFlag(n, _openMap, _showInChat);
                if (_teleportMe && CanTeleport(mob)) _teleportManager.TeleportToHunt(mob);
            }
            ImGui.PopStyleColor();
            ImGuiUtil.ImGui_HoveredToolTip($"{FormatWorld(mob.WorldName, mob.DataCenterName)}\n{MapHelpers.GetMapName(mob.TerritoryID)} ({mob.Position.X:0.0}, {mob.Position.Y:0.0})\n右键可选择、删除或调整顺序");
            if (ImGui.BeginPopupContextItem("HuntRowContext"))
            {
                if (ImGui.MenuItem(GuiResources.HuntTrainGuiText["ContextMenuSelect"])) _selectedIndex = n;
                if (ImGui.MenuItem("上移", enabled: row > 0)) { ApplyTrainChange(() => _trainManager.SwapTrainMobs(n, visible[row - 1])); listChanged = true; }
                if (ImGui.MenuItem("下移", enabled: row + 1 < visible.Count)) { ApplyTrainChange(() => _trainManager.SwapTrainMobs(n, visible[row + 1])); listChanged = true; }
                if (ImGui.MenuItem(GuiResources.HuntTrainGuiText["ContextMenuRemove"])) { ApplyTrainChange(() => _trainManager.TrainRemove(mob)); listChanged = true; }
                ImGui.EndPopup();
            }
            ImGui.TableNextColumn();
            HuntRosterPresentation.Status(true, mob.Dead, ImGuiHelpers.GlobalScale);
            if (_showMapName) { ImGui.TableNextColumn(); ImGui.TextUnformatted(MapHelpers.GetMapName(mob.TerritoryID)); }
            ImGui.TableNextColumn();
            if (_showTeleButtons)
            {
                var canTeleport = CanTeleport(mob);
                ImGui.BeginDisabled(!canTeleport);
                if (ImGui.Button($"{GuiResources.HuntTrainGuiText["TeleportButton"]}##teleport"))
                {
                    _teleportManager.TeleportToHunt(mob);
                    _trainManager.OpenMap(mob, _openMap);
                }
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(canTeleport ? "传送到该地图的最近水晶" : "请先切换到记录对应区服；传送不会跨服");
                ImGui.SameLine();
            }
            var reason = _navigation.GetDisabledReason(mob);
            ImGui.BeginDisabled(reason.Length != 0 || _patrolActive());
            if (ImGui.Button("移动")) _navigation.TryMoveTo(mob);
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(reason.Length != 0 ? reason : "调用国服 vnavmesh 前往该 A 怪点位；起飞后使用飞行路线，仅限同区服、同地图、同分线");
            if (_showLastSeen)
            {
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{Math.Max(0, (DateTime.UtcNow - mob.LastSeenUTC).TotalMinutes):0}m");
            }
            ImGui.TableNextColumn();
            var dead = mob.Dead;
            if (ImGui.Checkbox("##dead", ref dead))
            {
                if (!listChanged) _trainManager.SetDead(n, dead);
                if (dead && n == _selectedIndex) SelectNext();
            }
            ImGui.PopID();
            if (listChanged) break;
        }
        foreach (var expected in missing)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextDisabled($"{expected.Entry.Name} {expected.Instance.GetInstanceGlyph()}");
            var mapName = expected.TerritoryId == 0 ? "地图待识别" : MapHelpers.GetMapName(expected.TerritoryId);
            var lineName = expected.Instance == 0 ? "无分线或尚未确认分线" : $"{expected.Instance} 线";
            ImGuiUtil.ImGui_HoveredToolTip($"{expected.Entry.Name}\n{mapName} · {lineName}\n该服务器该分线尚无此怪记录；开启记录或找怪扫描后自动更新。");
            ImGui.TableNextColumn(); HuntRosterPresentation.Status(false, false, ImGuiHelpers.GlobalScale);
            if (_showMapName) { ImGui.TableNextColumn(); ImGui.TextDisabled(mapName); }
            ImGui.TableNextColumn(); ImGui.TextDisabled("--");
            if (_showLastSeen) { ImGui.TableNextColumn(); ImGui.TextDisabled("--"); }
            ImGui.TableNextColumn(); ImGui.TextDisabled("--");
        }
        ImGui.EndTable();
    }

    private bool CanTeleport(HuntTrainMob mob) => mob.WorldID != 0 && mob.WorldID == _trainManager.CurrentWorldId;

    private void DrawNavigationStatus()
    {
        if (_trainActionMessage.Length != 0) ImGui.TextWrapped(_trainActionMessage);
        if (_navigation.LastMessage.Length != 0) ImGui.TextWrapped(_navigation.LastMessage);
        if (_navigation.OwnsMovement && ImGui.Button("停止移动")) { if (_patrolActive()) _stopPatrol(); else _navigation.Stop(); }
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

    private void DrawDeleteModal()
    {
        var center = ImGui.GetWindowPos() + ImGui.GetWindowSize() / 2;
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(400, 200));

        if (ImGui.BeginPopupModal($"{GuiResources.HuntTrainGuiText["DeleteWindowTitle"]}##modal"))
        {
            ImGui.TextWrapped($"清空当前服务器「{VersionLabel}」标签中的记录？");
            ImGui.TextWrapped($"仅清空当前服务器的 {VersionLabel} 标签（{VisibleIndices.Count} 条）。清空当前所在服会暂停录制和找怪；其他版本、服务器的记录保留。右键服务器标签可删除整服记录。");

            ImGui.Dummy(new Vector2(0, 25));
            ImGui.Dummy(new Vector2(6, 0)); ImGui.SameLine();
            if (ImGui.Button(GuiResources.HuntTrainGuiText["DeleteWindowDeleteButton"]))
            {
                _trainManager.RemoveRecords(VisibleMobs, pauseRecording: true);
                _selectedIndex = 0;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine(); ImGui.Dummy(new Vector2(16, 0));
            ImGui.SameLine();
            if (ImGui.Button(GuiResources.HuntTrainGuiText["CancelButton"]))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
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
                    ImGui.TextUnformatted(GuiResources.HuntTrainGuiText["LastSeenHeader"]);
                    foreach (var m in _importedTrain)
                    {   //green text if new mob, grey text if exists.
                        ImGui.PushStyleColor(ImGuiCol.Text,
                            !_trainManager.ContainsMob(m)
                                ? new Vector4(0.1647f, 1f, 0.647f, 1f) //greenish
                                : new Vector4(0.51f, 0.51f, 0.51f, 1f)); //grey
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

            if (ImGui.Checkbox(GuiResources.HuntTrainGuiText["UpdateLastSeenCheckbox"], ref _importUpdateTime))
            {
                _importNew = true;
                _importAll = false;
            }
            ;
            ImGui.SameLine(); ImGuiUtil.ImGui_HelpMarker(GuiResources.HuntTrainGuiText["UpdateLastSeenCheckboxToolTip"]);

            ImGui.Dummy(new Vector2(0, 12));
            ImGui.Dummy(new Vector2(6, 0));
            ImGui.SameLine();
            if (ImGui.Button(GuiResources.HuntTrainGuiText["ImportButton"], new Vector2(80, 0)))
            {
                if (_importNew || _importUpdateTime || _importAll)
                {
                    ImportTrainData();
                    _importedTrain.Clear();
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine(); ImGui.Dummy(new Vector2(6, 0));
            ImGui.SameLine();
            if (ImGui.Button(GuiResources.HuntTrainGuiText["CancelButton"], new Vector2(80, 0)))
            {
                _importedTrain.Clear();
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }

    private void ImportTrainData()
    {
        _selectedIndex = 0;
        if (_importAll)
        {
            _trainManager.ImportTrainAll();
            return;
        }
        _trainManager.ImportTrainNew(_importUpdateTime);
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

    //based off of https://github.com/ocornut/imgui/blob/docking/imgui_demo.cpp#L2337
    /// <summary>
    /// no longer used since ui rework
    /// </summary>
    /// <param name="attributeToDisplay"></param>
    private void SelectableFromList(HuntTrainMobAttribute attributeToDisplay)
    {
        for (int n = 0; n < _mobList.Count; n++)
        {
            var mob = _mobList[n];
            var label = GetAttributeFromMob(attributeToDisplay, mob);

            if (mob.Dead) ImGui.PushStyleColor(ImGuiCol.Text, _deadTextColour);
            else ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One); //white

            if (n == _selectedIndex) ImGui.Selectable($"{label}", true, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X, 23f * ImGuiHelpers.GlobalScale));
            else ImGui.Selectable($"{label}", false, ImGuiSelectableFlags.None, new Vector2(ImGui.GetContentRegionAvail().X, 23f * ImGuiHelpers.GlobalScale));

            if (ImGui.IsItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                _trainManager.SendTrainFlag(n, _openMap, _showInChat);
                if (_teleportMe) _teleportManager.TeleportToHunt(mob);
            }

            if (ImGui.BeginPopupContextItem($"ContextMenu##{mob.Name}:{mob.Instance}", ImGuiPopupFlags.MouseButtonRight))
            {
                ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One); //white
                if (ImGui.MenuItem(GuiResources.HuntTrainGuiText["ContextMenuSelect"], true)) _selectedIndex = n;
                ImGui.MenuItem("   ---", false);
                if (ImGui.MenuItem(GuiResources.HuntTrainGuiText["ContextMenuRemove"], true)) _trainManager.TrainRemove(mob);
                ImGui.EndPopup();
                ImGui.PopStyleColor();
            }

            if (ImGui.IsItemActive() && !ImGui.IsItemHovered())
            {
                int n_next = n + (ImGui.GetMouseDragDelta(ImGuiMouseButton.Left).Y < 0f ? -1 : 1);
                if (n_next >= 0 && n_next < _mobList.Count)
                {
                    _mobList[n] = _mobList[n_next];
                    _mobList[n_next] = mob;
                    ImGui.ResetMouseDragDelta();
                }
            }

            ImGui.Separator();
            ImGui.PopStyleColor(); // pop style text colour
        }
    }

    private string GetAttributeFromMob(HuntTrainMobAttribute attribute, HuntTrainMob mob) =>
        attribute switch
        {
            HuntTrainMobAttribute.Name =>
                _showMapName ? $"「{MapHelpers.GetMapName(mob.TerritoryID)}」{mob.Name}{mob.Instance.GetInstanceGlyph()}"
                : $"{mob.Name}{mob.Instance.GetInstanceGlyph()}",
            HuntTrainMobAttribute.LastSeen => $"{(DateTime.Now.ToUniversalTime() - mob.LastSeenUTC).TotalMinutes:0.}m",
            HuntTrainMobAttribute.Position => $"({mob.Position.X:0.0}, {mob.Position.Y:0.0})"
        };

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

    private enum HuntTrainMobAttribute
    {
        Name, Position, LastSeen
    }

}
