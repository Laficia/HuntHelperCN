using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace HuntHelper.Gui.Native;

/// <summary>Local native prototype. All widgets belong to the addon's root and die with it.</summary>
internal sealed class NativeTrainWindow : NativeAddon
{
    private sealed class Item
    {
        internal required NativeTrainText Name;
        internal required NativeTrainText Map;
        internal required TextButtonNode Teleport;
        internal required TextButtonNode Move;
        internal required TextButtonNode Coordinates;
        internal required CheckboxNode Dead;
        internal NativeTrainRow? Row;
        internal string? GroupKey;
        internal bool Header;
        internal bool Binding;
        internal void Hide()
        { Name.IsVisible = Map.IsVisible = Teleport.IsVisible = Move.IsVisible = Coordinates.IsVisible = Dead.IsVisible = false; Row = null; GroupKey = null; }
    }
    private readonly Action<NativeTrainRequest> dispatch;
    private readonly Action<Vector2> saveSize;
    private readonly List<TextButtonNode> worldButtons = new(), versionButtons = new(), tools = new();
    private readonly List<Item> items = new();
    private readonly HashSet<string> collapsed = new();
    private NativeTrainSnapshot? snapshot;
    private ResNode? body;
    private ScrollingNode<ResNode>? scroll;
    private TextNode? worldLabel, footer, empty;
    private TextButtonNode? finder, recording, macros, more, current, legacy;
    private TextureButtonNode? resize;
    private bool showTools, setup, suppressClose, dragDone;
    private readonly NativePressGesture press = new();
    private NodeBase? pressNode;
    private static readonly string BuildVersion = typeof(NativeTrainWindow).Assembly.GetName().Version?.ToString() ?? "";
    private ViewportEventListener? pointer;
    private Vector2 startMouse, startSize, lastSize;
    private string dragKind = "", dragKey = "";
    private uint dragWorld;
    private long suppressClickUntil;
    private ResNode? contextPanel;
    private ColorImageNode? contextBackground, dropCue;
    private readonly List<TextButtonNode> contextButtons = new();
    private ViewportEventListener? contextPointer;
    private bool contextClosePending;
    private long contextOpenedAt;
    private Vector2 pointerPosition;
    private long lastAutoScroll;
    private float autoScrollRemainder;

    internal NativeTrainWindow(Action<NativeTrainRequest> dispatch, Action<Vector2> saveSize)
    { this.dispatch = dispatch; this.saveSize = saveSize; }

    internal void Apply(NativeTrainSnapshot value)
    { snapshot = value; if (setup && pointer == null) Layout(); }

    internal void CloseFromModel()
    { suppressClose = true; Close(); }

    private void Send(NativeTrainAction action, string key = "", bool value = false)
    {
        if (snapshot != null && Environment.TickCount64 >= suppressClickUntil)
            dispatch(new(action, snapshot.World, key, Value: value));
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        base.OnSetup(addon, values);
        suppressClose = false; showTools = false;
        worldButtons.Clear(); versionButtons.Clear(); tools.Clear(); items.Clear();
        body = new ResNode(); body.AttachNode(this);
        finder = Button(body, "找怪", () => Send(NativeTrainAction.Finder));
        recording = Button(body, "开始录制", () => Send(NativeTrainAction.Recording));
        macros = Button(body, "预设宏", () => Send(NativeTrainAction.Macros));
        more = Button(body, "更多", () => { showTools = !showTools; Layout(); });
        legacy = Button(body, "原版窗口", () => Send(NativeTrainAction.Legacy));
        worldLabel = Label(body, "");
        current = Button(body, "当前服", () => Send(NativeTrainAction.CurrentWorld));
        foreach (var (label, action) in new[] {
            ("导入", NativeTrainAction.Import), ("导出", NativeTrainAction.Export),
            ("备份撤销", NativeTrainAction.Backups), ("设置", NativeTrainAction.Settings),
            ("清理死亡", NativeTrainAction.ClearDead), ("恢复存活", NativeTrainAction.Revive),
            ("清空标签", NativeTrainAction.Clear), (snapshot?.Rank == "S" ? "A怪记录" : "S怪记录", NativeTrainAction.OtherRank),
            ("原版窗口", NativeTrainAction.Legacy) })
            tools.Add(Button(body, label, () => Send(action)));
        scroll = new ScrollingNode<ResNode> { AutoHideScrollBar = true, ScrollSpeed = 34 };
        scroll.AttachNode(body);
        empty = Label(scroll.ContentNode, snapshot?.Rank == "S" ? "暂无 S 怪记录\n开启录制后，可扫描附近 S 怪或录入 Sonar 定位。" : "暂无记录\n开启录制，或点击找怪开始扫描。", 14);
        empty.AddTextFlags(TextFlags.MultiLine);
        footer = Label(body, "", 12);
        footer.AddTextFlags(TextFlags.MultiLine);
        resize = new TextureButtonNode {
            Size = new(18, 18), TexturePath = "ui/uld/ChatLog.tex",
            TextureCoordinates = new(32, 34), TextureSize = new(18, 18), TextTooltip = "拖动调整窗口大小" };
        resize.ImageNode.ImageNodeFlags = ImageNodeFlags.FlipV;
        resize.AddEvent(AtkEventType.MouseDown, (_, _, _, evt, data) => BeginPointer("resize", "", resize, evt, data));
        resize.AttachNode(this);
        dropCue = new ColorImageNode { IsVisible = false };
        dropCue.AttachNode(this);
        setup = true; Layout();
    }

    private static TextNode Label(NodeBase parent, string text, uint fontSize = 14)
    {
        var node = new TextNode { String = text, FontSize = fontSize, Size = new(200, 24), TextColorID = 8 };
        node.AddTextFlags(TextFlags.Ellipsis);
        node.AttachNode(parent); return node;
    }

    private static TextButtonNode Button(NodeBase parent, string text, Action click)
    {
        var node = new TextButtonNode { String = text, Size = new(70, 28), OnClick = click };
        node.LabelNode.FontSize = 13;
        node.AttachNode(parent); return node;
    }

    private static void Place(NodeBase node, float x, float y, float width, float height = 28)
    {
        node.Position = new(x, y); node.Size = new(MathF.Max(16, width), height); node.IsVisible = true;
        // Native buttons own their label positions through their timelines. Overriding
        // those positions here would make the label jump back when hover begins.
        if (node is NativeTrainText text) text.Arrange();
    }

    private float Tabs(List<TextButtonNode> nodes, IReadOnlyList<NativeTrainTab> tabs, float y, float width, bool worlds)
    {
        while (nodes.Count < tabs.Count)
        {
            var button = Button(body!, "", () => { });
            nodes.Add(button);
            unsafe { button.AddEvent(AtkEventType.MouseDown, (_, _, _, evt, data) => {
                if (data == null || snapshot == null || !tabKeys.TryGetValue(button, out var id)) return;
                if (data->MouseData.ButtonId == 1 && uint.TryParse(id, out var tabId))
                    ShowContext(new(data->MouseData.PosX, data->MouseData.PosY), NativeTrainMenus.Tab(snapshot.World, tabId, worlds));
                else BeginPointer(worlds ? "world" : "version", id, button, evt, data);
            }); }
        }
        float x = 0;
        for (int i = 0; i < nodes.Count; i++)
        {
            var button = nodes[i];
            button.IsVisible = i < tabs.Count;
            if (i >= tabs.Count) continue;
            var tab = tabs[i];
            var w = Math.Clamp(tab.Text.Length * 10 + 30, 76, width);
            if (x > 0 && x + w > width) { x = 0; y += 30; }
            Place(button, x, y, w);
            button.String = tab.Text; button.IsChecked = tab.Selected;
            button.LabelNode.TextColor = tab.Selected ? Accent(true) : ColorHelper.GetColor(50);
            // Keep IDs in a separate managed lookup; tooltips contain only user-facing text.
            tabKeys[button] = tab.Id.ToString();
            button.TextTooltip = "左键选择；按住拖动排序；右键选择、调整顺序或删除";
            // Selection is dispatched once on a matching left release by OnPointer.
            button.OnClick = () => { };
            x += w + 4;
        }
        return y + (tabs.Count == 0 ? 0 : 32);
    }
    private readonly Dictionary<TextButtonNode, string> tabKeys = new();

    private Item CreateItem()
    {
        var root = scroll!.ContentNode;
        var item = new Item { Name = new NativeTrainText(), Map = new NativeTrainText(),
            Teleport = Button(root, "传送", () => { }), Move = Button(root, "移动", () => { }), Coordinates = Button(root, "坐标", () => { }),
            Dead = new CheckboxNode { Size = new(22, 22) } };
        item.Dead.AttachNode(root);
        item.Name.AttachNode(root); item.Map.AttachNode(root);
        item.Map.LabelNode.FontSize = 12;
        item.Map.OnClick = () => { if (item.Row is { Missing: false } row) Send(NativeTrainAction.Map, row.Key); };
        item.Name.OnClick = () => {
            if (Environment.TickCount64 < suppressClickUntil) return;
            if (item.Header && item.GroupKey != null) { if (!collapsed.Add(item.GroupKey)) collapsed.Remove(item.GroupKey); Layout(); }
            // Real rows are selected by the release gesture; headers only expand/collapse.
        };
        item.Teleport.OnClick = () => { if (item.Row != null) Send(NativeTrainAction.Teleport, item.Row.Key); };
        item.Move.OnClick = () => { if (item.Row != null) Send(NativeTrainAction.Move, item.Row.Key); };
        item.Coordinates.OnClick = () => { if (item.Row != null) Send(NativeTrainAction.Coordinates, item.Row.Key); };
        item.Dead.OnClick = value => { if (!item.Binding && item.Row != null) Send(NativeTrainAction.Dead, item.Row.Key, value); };
        unsafe { item.Name.Hitbox.AddEvent(AtkEventType.MouseDown, (_, _, _, evt, data) => {
            if (data == null || snapshot == null || item.Header || item.Row is not { Missing: false } row) return;
            if (data->MouseData.ButtonId == 1)
                ShowContext(new(data->MouseData.PosX, data->MouseData.PosY), NativeTrainMenus.Row(snapshot.World, row.Key));
            else BeginPointer("row", row.Key, item.Name.Hitbox, evt, data);
        });
        item.Map.Hitbox.AddEvent(AtkEventType.MouseDown, (_, _, _, _, data) => {
            if (data != null && data->MouseData.ButtonId == 1 && snapshot != null && item.Row is { Missing: false } row)
                ShowContext(new(data->MouseData.PosX, data->MouseData.PosY), NativeTrainMenus.Map(snapshot.World, row.Key));
        });
        item.Name.Hitbox.AddLinkedEvent(scroll.ScrollBarNode, 5, AtkEventType.MouseWheel);
        item.Map.Hitbox.AddLinkedEvent(scroll.ScrollBarNode, 5, AtkEventType.MouseWheel);
        }
        items.Add(item); return item;
    }

    private void Layout()
    {
        if (!setup || snapshot == null || body == null || scroll == null) return;
        body.Position = ContentStartPosition;
        var width = MathF.Max(310, Size.X - ContentStartPosition.X * 2);
        body.Size = new(width, MathF.Max(180, Size.Y - ContentStartPosition.Y - 12));
        finder!.String = snapshot.Patrol ? "停止找怪" : snapshot.Moving ? "停止移动" : "找怪";
        finder.IsVisible = snapshot.Rank == "A" || snapshot.Moving;
        recording!.String = snapshot.Recording ? "停止录制" : "开始录制";
        recording.IsChecked = snapshot.Recording;
        var toolbar = NativeTrainToolbar.Arrange(width, finder.IsVisible);
        foreach (var slot in toolbar)
        {
            var node = slot.Key switch { "finder" => finder, "recording" => recording, "macros" => macros, "more" => more, _ => legacy };
            Place(node!, slot.Position.X, slot.Position.Y, slot.Width);
        }
        legacy!.TextTooltip = "切换为紧凑原版界面；也可使用 /hht legacy";
        more!.IsChecked = showTools;
        float x = 0, y = toolbar.Max(slot => slot.Position.Y) + 34;
        foreach (var tool in tools) tool.IsVisible = showTools;
        if (showTools)
        {
            x = 0;
            foreach (var tool in tools) {
                var w = 90f;
                if (x > 0 && x + w > width) { x = 0; y += 30; }
                Place(tool, x, y, w); x += w + 4;
            }
            y += 34;
        }
        Place(worldLabel!, 2, y, width - 80, 24); worldLabel!.String = $"当前区服：{snapshot.CurrentWorld}";
        Place(current!, width - 74, y, 74);
        y = Tabs(worldButtons, snapshot.Worlds, y + 32, width, true);
        y = Tabs(versionButtons, snapshot.Versions, y + 2, width, false) + 4;
        var height = MathF.Max(60, Size.Y - ContentStartPosition.Y - y - 52);
        scroll.Position = new(0, y); scroll.Size = new(width, height);
        var listWidth = width - 16;
        var content = scroll.ContentNode;
        IReadOnlyList<NativeTrainGroup> grouped = snapshot.GroupByMap ? snapshot.Groups
            : new[] { new NativeTrainGroup(0, 0, "", snapshot.Rows) };
        int used = 0; float rowY = 0;
        foreach (var group in grouped)
        {
            var key = $"{snapshot.World}/{group.Territory}/{group.Instance}";
            Item item;
            if (snapshot.GroupByMap)
            {
                item = used < items.Count ? items[used] : CreateItem(); used++;
                item.Hide(); item.Header = true; item.GroupKey = key;
                Place(item.Name, 0, rowY, listWidth, 28);
                item.Name.String = $"{(collapsed.Contains(key) ? "+" : "−")}  {group.Title}   {group.Rows.Count(r => !r.Missing)}";
                item.Name.LabelNode.AlignmentType = AlignmentType.Left;
                item.Name.Paint(Accent(), Accent());
                item.Name.IsEnabled = true;
                item.Name.IsChecked = false;
                item.Name.TextTooltip = "点击折叠或展开此地图";
                rowY += 34;
                if (collapsed.Contains(key)) continue;
            }
            foreach (var row in group.Rows)
            {
                item = used < items.Count ? items[used] : CreateItem(); used++;
                item.Hide(); item.Header = false; item.Row = row;
                var textWidth = MathF.Max(72, listWidth - 220);
                var mapWidth = snapshot.GroupByMap ? 0 : Math.Clamp(textWidth * .44f, 42, 148);
                var nameWidth = snapshot.GroupByMap ? textWidth : MathF.Max(32, textWidth - mapWidth - 8);
                Place(item.Name, 4, rowY, nameWidth);
                item.Name.String = row.Name + (row.Missing ? " · 未发现" : row.Dead ? " · 已死亡" : ""); item.Name.TextTooltip = row.Tooltip;
                item.Name.IsChecked = row.Selected; item.Name.IsEnabled = !row.Missing;
                item.Name.LabelNode.AlignmentType = AlignmentType.Left;
                item.Name.Paint(row.Missing ? Semantic(false) : row.Dead ? Muted() : ColorHelper.GetColor(8), Accent());
                if (!snapshot.GroupByMap)
                {
                    Place(item.Map, nameWidth + 12, rowY, mapWidth);
                    item.Map.String = row.Map + (row.Instance == 0 ? "" : $" · {row.Instance}线");
                    item.Map.IsChecked = false; item.Map.IsEnabled = !row.Missing;
                    item.Map.TextTooltip = row.Map + "\n点击在游戏地图中查看此记录的点位";
                    item.Map.Paint(Muted(), Accent());
                }
                if (!row.Missing)
                {
                    var actionX = listWidth - 212;
                    Place(item.Teleport, actionX, rowY, 58); item.Teleport.IsEnabled = row.CanTeleport;
                    item.Teleport.TextTooltip = row.CanTeleport ? "正常水晶传送" : "仅可传送当前所在区服的记录";
                    Place(item.Move, actionX + 60, rowY, 58); item.Move.IsEnabled = row.CanMove;
                    item.Move.TextTooltip = row.MoveReason.Length == 0 ? "通过 vnavmesh 前往该点位" : row.MoveReason;
                    Place(item.Coordinates, actionX + 120, rowY, 58);
                    item.Coordinates.TextTooltip = $"发送 {snapshot.Rank} 类预设宏";
                    Place(item.Dead, listWidth - 24, rowY, 28, 28);
                    item.Binding = true; item.Dead.IsChecked = row.Dead; item.Binding = false;
                    item.Dead.TextTooltip = "标记已死亡";
                }
                rowY += 34;
            }
            rowY += 8;
        }
        for (int i = used; i < items.Count; i++) items[i].Hide();
        empty!.IsVisible = used == 0;
        if (used == 0) Place(empty, 12, 18, listWidth - 24, 70);
        content.Size = new(listWidth, MathF.Max(rowY, height)); scroll.RecalculateSizes();
        Place(footer!, 2, y + height + 5, width - 10, 32);
        var state = snapshot.Patrol ? "找怪中" : snapshot.Recording ? "录制中" : "录制关闭";
        footer!.String = $"{state} · 已录入 {snapshot.Rows.Count(r => !r.Missing)} · v{BuildVersion}";
        footer.TextTooltip = snapshot.Status.Length == 0 ? "扫描和 Sonar 录制保存到记录对应的服务器" : snapshot.Status;
        if (snapshot.Status.Contains("无法") || snapshot.Status.Contains("失败") || snapshot.Status.Contains("错误"))
            footer.String = $"{state} · 已录入 {snapshot.Rows.Count(r => !r.Missing)} · v{BuildVersion}\n{snapshot.Status}";
        resize!.Position = Size - new Vector2(23, 23);
        lastSize = Size;
        if (contextBackground != null) contextBackground.Color = DarkText() ? new(.9f, .89f, .86f, 1) : new(.07f, .10f, .16f, 1);
    }

    private unsafe void ShowContext(Vector2 screen, IReadOnlyList<NativeMenuChoice> choices)
    {
        if (!setup || snapshot == null) return;
        if (contextPanel == null)
        {
            contextPanel = new ResNode(); contextPanel.AttachNode(this);
            contextBackground = new ColorImageNode(); contextBackground.AttachNode(contextPanel);
            for (int i = 0; i < 4; i++) contextButtons.Add(Button(contextPanel, "", () => { }));
        }
        contextPanel.IsVisible = true; contextClosePending = false; contextOpenedAt = Environment.TickCount64;
        contextPanel.Size = new(174, choices.Count * 32 + 12);
        var scale = InternalAddon == null ? 1 : MathF.Max(.1f, InternalAddon->Scale);
        contextPanel.Position = Vector2.Clamp((screen - RootNode.ScreenPosition) / scale,
            new(8, ContentStartPosition.Y), Vector2.Max(new(8, ContentStartPosition.Y), Size - contextPanel.Size - new Vector2(8)));
        contextBackground!.Size = contextPanel.Size;
        contextBackground.Color = DarkText() ? new(.9f, .89f, .86f, 1) : new(.07f, .10f, .16f, 1);
        for (int i = 0; i < contextButtons.Count; i++)
        {
            var button = contextButtons[i]; button.IsVisible = i < choices.Count;
            if (i >= choices.Count) continue;
            var choice = choices[i];
            Place(button, 6, 6 + i * 32, 162); button.String = choice.Label;
            button.OnClick = () => { HideContext(); dispatch(choice.Request); };
        }
        if (contextPointer != null) return;
        contextPointer = new ViewportEventListener((_, _, _, _, data) => {
            if (data == null || contextPanel == null || !contextPanel.IsVisible || Environment.TickCount64 - contextOpenedAt < 150) return;
            var mouse = new Vector2(data->MouseData.PosX, data->MouseData.PosY);
            var currentScale = InternalAddon == null ? 1 : MathF.Max(.1f, InternalAddon->Scale);
            if (!NativeDragGeometry.Contains(mouse, contextPanel.ScreenPosition, contextPanel.Size, currentScale)) contextClosePending = true;
        });
        contextPointer.AddEvent(AtkEventType.MouseDown, contextPanel);
    }

    private void HideContext()
    { if (contextPanel != null) contextPanel.IsVisible = false; contextClosePending = true; }

    // Follow the player's native light/dark theme instead of imposing white text on a light window.
    private static bool DarkText(bool button = false)
    {
        var color = ColorHelper.GetColor(button ? 50u : 8u);
        return color.X * .2126f + color.Y * .7152f + color.Z * .0722f < .5f;
    }
    private static Vector4 Accent(bool button = false) => DarkText(button) ? new(.38f, .23f, .04f, 1) : new(1, .86f, .58f, 1);
    private static Vector4 Muted(bool button = false) => DarkText(button) ? new(.38f, .38f, .38f, 1) : new(.63f, .63f, .63f, 1);
    private static Vector4 Semantic(bool alive, bool button = false) => DarkText(button)
        ? alive ? new(.08f, .36f, .23f, 1) : new(.67f, .08f, .08f, 1)
        : alive ? new(.45f, .82f, .65f, 1) : new(1, .35f, .32f, 1);

    private unsafe void BeginPointer(string kind, string key, NodeBase source, AtkEvent* evt, AtkEventData* data)
    {
        if (data == null || data->MouseData.ButtonId != 0 || snapshot == null) return;
        HideContext();
        EndPointer();
        dragKind = kind; dragKey = source is TextButtonNode tab && kind != "row" && tabKeys.TryGetValue(tab, out var tabId) ? tabId : key;
        dragWorld = snapshot.World; startMouse = new(data->MouseData.PosX, data->MouseData.PosY); startSize = Size;
        pressNode = source; press.Begin(startMouse);
        pointerPosition = startMouse; lastAutoScroll = Environment.TickCount64; autoScrollRemainder = 0;
        pointer = new ViewportEventListener(OnPointer);
        pointer.AddEvent(AtkEventType.MouseMove, source); pointer.AddEvent(AtkEventType.MouseUp, source);
        if (kind == "resize" && evt != null) evt->SetEventIsHandled();
    }

    private unsafe void OnPointer(AtkEventListener* listener, AtkEventType type, int param, AtkEvent* evt, AtkEventData* data)
    {
        if (data == null || dragDone || snapshot == null) return;
        if (type == AtkEventType.MouseUp && data->MouseData.ButtonId != 0) return;
        var mouse = new Vector2(data->MouseData.PosX, data->MouseData.PosY);
        pointerPosition = mouse;
        var delta = mouse - startMouse;
        press.Move(mouse);
        if (press.Dragging) suppressClickUntil = Environment.TickCount64 + 250;
        if (dragKind == "resize" && type == AtkEventType.MouseMove)
        {
            var scale = InternalAddon == null ? 1 : MathF.Max(.1f, InternalAddon->Scale);
            Size = Vector2.Clamp(startSize + delta / scale, new(350, 300), new(1500, 1400));
            Layout();
        }
        if (type == AtkEventType.MouseUp) dragDone = true;
        if (dragKind == "resize") { if (type == AtkEventType.MouseUp) saveSize(Size); return; }
        if (type != AtkEventType.MouseMove && type != AtkEventType.MouseUp) return;
        if (dragWorld != snapshot.World) { press.Cancel(); return; }
        var nativeScale = InternalAddon == null ? 1 : MathF.Max(.1f, InternalAddon->Scale);
        if (type == AtkEventType.MouseUp && press.Release(mouse, pressNode != null &&
            NativeDragGeometry.Contains(mouse, pressNode.ScreenPosition, pressNode.Size, nativeScale)))
        {
            if (dragKind == "world" && uint.TryParse(dragKey, out var world)) dispatch(new(NativeTrainAction.World, world));
            else dispatch(new(dragKind == "row" ? NativeTrainAction.SelectOnly : NativeTrainAction.Version, dragWorld, dragKey));
            return;
        }
        if (!press.Dragging) return;
        suppressClickUntil = Environment.TickCount64 + 250;
        if (dropCue != null) dropCue.IsVisible = false;
        if (dragKind == "row" && scroll != null && !NativeDragGeometry.Contains(mouse, scroll.ScreenPosition, scroll.Size, nativeScale)) return;
        IEnumerable<(NodeBase Node, string Key)> targets = dragKind == "row"
            ? items.Where(i => !i.Header && i.Row is { Missing: false } && i.Name.IsVisible).Select(i => ((NodeBase)i.Name, i.Row!.Key))
            : (dragKind == "world" ? worldButtons : versionButtons).Where(n => n.IsVisible).Select(n => ((NodeBase)n, tabKeys[n]));
        foreach (var (node, key) in targets)
        {
            var targetSize = dragKind == "row" && scroll != null ? new Vector2(scroll.ContentNode.Width - 4, 34) : node.Size;
            if (!NativeDragGeometry.Contains(mouse, node.ScreenPosition, targetSize, nativeScale)) continue;
            var after = NativeDragGeometry.After(mouse, node.ScreenPosition, targetSize, nativeScale, dragKind == "row");
            if (key == dragKey) break;
            if (type == AtkEventType.MouseMove && dropCue != null)
            {
                dropCue.Position = (node.ScreenPosition - RootNode.ScreenPosition) / nativeScale +
                    (dragKind == "row" ? new Vector2(0, after ? targetSize.Y : 0) : new Vector2(after ? targetSize.X : 0, 0));
                dropCue.Size = dragKind == "row" ? new(targetSize.X, 2) : new(2, targetSize.Y);
                dropCue.Color = DarkText() ? new(.03f, .38f, .42f, .85f) : new(.3f, .85f, .9f, .85f);
                dropCue.IsVisible = true; break;
            }
            if (type != AtkEventType.MouseUp) break;
            dispatch(new(dragKind == "row" ? NativeTrainAction.ReorderRow : dragKind == "world" ? NativeTrainAction.ReorderWorld : NativeTrainAction.ReorderVersion,
                dragWorld, dragKey, key, after)); break;
        }
    }

    private void EndPointer() { pointer?.Dispose(); pointer = null; pressNode = null; press.Cancel(); dragDone = false; if (dropCue != null) dropCue.IsVisible = false; }
    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        base.OnUpdate(addon); if (dragDone) EndPointer(); if (setup && Size != lastSize) Layout();
        if (contextClosePending) { if (contextPanel != null) contextPanel.IsVisible = false; contextPointer?.Dispose(); contextPointer = null; contextClosePending = false; }
        if (setup) foreach (var item in items) { item.Name.Animate(); item.Map.Animate(); }
        if (pointer != null && press.Dragging && dragKind == "row" && scroll != null && InternalAddon != null)
        {
            var now = Environment.TickCount64;
            var direction = NativeDragGeometry.ScrollDirection(pointerPosition, scroll.ScreenPosition, scroll.Size, MathF.Max(.1f, InternalAddon->Scale));
            autoScrollRemainder += direction * Math.Clamp(now - lastAutoScroll, 0, 50) * .3f;
            lastAutoScroll = now;
            var step = (int)autoScrollRemainder;
            if (step != 0) { scroll.ScrollBarNode.ScrollPosition = Math.Clamp(scroll.ScrollBarNode.ScrollPosition + step, 0, scroll.ScrollBarNode.ScrollMaxPosition); autoScrollRemainder -= step; }
        }
    }
    protected override unsafe void OnHide(AtkUnitBase* addon)
    { EndPointer(); contextPointer?.Dispose(); contextPointer = null; saveSize(Size); if (!suppressClose) dispatch(new(NativeTrainAction.Close)); base.OnHide(addon); }
    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        setup = false; EndPointer(); worldButtons.Clear(); versionButtons.Clear(); tools.Clear(); items.Clear(); tabKeys.Clear();
        contextPointer?.Dispose(); contextPointer = null; contextButtons.Clear(); contextPanel = null; contextBackground = null; dropCue = null;
        body = null; scroll = null; resize = null; base.OnFinalize(addon);
    }
}
