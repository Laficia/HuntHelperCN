using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.BaseTypes;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace HuntHelper.Gui.Native;

/// <summary>Shared native form for finder and macro editor. Native events enqueue model requests.</summary>
internal sealed class NativeFormWindow : NativeAddon
{
    private sealed class MacroInput : TextInputNode
    {
        internal MacroInput() : base(true) { MaxCharacters = 6000; MaxBytes = 24000; MaxLines = 255; ShowLimitText = false; }
    }
    private sealed class Field
    {
        internal required ResNode Root;
        internal required TextNode Label;
        internal required NativeFormRow Row;
        internal CheckboxNode? Check;
        internal NumericInputNode? Number;
        internal StringDropDownNode? Choice;
        internal TextInputNode? Input;
        internal ScrollingNode<ResNode>? List;
        internal NativeTrainText? DropZone;
        internal readonly List<CheckboxNode> Checks = new();
        internal readonly List<TextButtonNode> Buttons = new();
        internal bool Binding, Used;
    }
    private readonly Action<NativeFormRequest> dispatch;
    private readonly Action<Vector2> saveSize;
    private NativeFormSnapshot snapshot = NativeFormSnapshot.Hidden;
    private readonly Dictionary<string, Field> fields = new();
    private readonly List<TextButtonNode> toolbar = new(), menuButtons = new();
    private readonly List<(TextButtonNode Node, string Id, bool Collection)> dragTargets = new();
    private ResNode? body, menu;
    private ScrollingNode<ResNode>? scroll;
    private TextNode? status;
    private TextureButtonNode? resize;
    private ColorImageNode? menuBackground, cue;
    private ViewportEventListener? pointer, menuPointer;
    private bool setup, suppressClose, dragDone, dragging, resizing, closeMenu;
    private Vector2 startMouse, startSize, mousePosition, lastSize;
    private string dragId = "";
    private long suppressClickUntil, menuOpenedAt, lastScroll;
    private float scrollRemainder;

    internal NativeFormWindow(Action<NativeFormRequest> dispatch, Action<Vector2> saveSize)
    { this.dispatch = dispatch; this.saveSize = saveSize; }
    internal void Apply(NativeFormSnapshot value)
    { snapshot = value; if (setup && pointer == null) Layout(); }
    internal void CloseFromModel() { suppressClose = true; Close(); }
    private void Send(string key, string value = "", string target = "", bool after = true)
    { if (Environment.TickCount64 >= suppressClickUntil) dispatch(new(key, value, target, after)); }

    private static TextNode Text(NodeBase parent)
    {
        var node = new TextNode { FontSize = 13, TextColorID = 8, AlignmentType = AlignmentType.Left };
        node.AddTextFlags(TextFlags.Ellipsis); node.AttachNode(parent); return node;
    }
    private static TextButtonNode Button(NodeBase parent)
    {
        var node = new TextButtonNode { Size = new(90, 28) };
        node.LabelNode.FontSize = 13; node.LabelNode.AddTextFlags(TextFlags.Ellipsis);
        node.AttachNode(parent); return node;
    }
    private static void Place(NodeBase node, float x, float y, float width, float height = 28)
    {
        var size = new Vector2(MathF.Max(16, width), height);
        node.Position = new(x, y); if (node.Size != size) node.Size = size; node.IsVisible = true;
        if (node is NativeTrainText text) text.Arrange();
    }
    private static bool Light
    {
        get { var color = ColorHelper.GetColor(8); return color.X * .2126f + color.Y * .7152f + color.Z * .0722f < .5f; }
    }
    private static Vector4 Accent => Light ? new(.32f, .22f, .05f, 1) : new(1, .86f, .58f, 1);
    private static float ButtonWidth(string label)
    {
        var width = 48f;
        foreach (var rune in label.EnumerateRunes()) width += rune.Value < 128 ? 8 : 15;
        return Math.Clamp(width, 88, 210);
    }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        base.OnSetup(addon, values); suppressClose = false;
        fields.Clear(); toolbar.Clear(); dragTargets.Clear(); menuButtons.Clear();
        body = new ResNode(); body.AttachNode(this);
        scroll = new ScrollingNode<ResNode> { AutoHideScrollBar = true, ScrollSpeed = 32 }; scroll.AttachNode(body);
        status = Text(body); status.AddTextFlags(TextFlags.MultiLine);
        resize = new TextureButtonNode { Size = new(18), TexturePath = "ui/uld/ChatLog.tex", TextureCoordinates = new(32, 34), TextureSize = new(18), TextTooltip = "拖动调整窗口大小" };
        resize.ImageNode.ImageNodeFlags = ImageNodeFlags.FlipV;
        resize.AddEvent(AtkEventType.MouseDown, (_, _, _, _, data) => {
            if (data != null && data->MouseData.ButtonId == 0) BeginPointer(resize, "", new(data->MouseData.PosX, data->MouseData.PosY), true);
        }); resize.AttachNode(this);
        cue = new ColorImageNode { IsVisible = false }; cue.AttachNode(this);
        setup = true; Layout();
    }

    private Field Create(NativeFormRow row, string poolKey)
    {
        var field = new Field { Root = new ResNode(), Label = null!, Row = row };
        field.Root.AttachNode(scroll!.ContentNode); field.Label = Text(field.Root);
        switch (row.Kind)
        {
            case NativeFormKind.Check:
                field.Check = new CheckboxNode { DisableAutoResize = true }; field.Check.Label.FontSize = 13;
                field.Check.AttachNode(field.Root);
                field.Check.OnClick = value => { if (!field.Binding) Send(field.Row.Key, value.ToString()); }; break;
            case NativeFormKind.Number:
                field.Number = new NumericInputNode(); field.Number.AttachNode(field.Root);
                field.Number.OnValueUpdate = value => { if (!field.Binding) Send(field.Row.Key, value.ToString(CultureInfo.InvariantCulture)); }; break;
            case NativeFormKind.Choice:
                field.Choice = new StringDropDownNode { MaxListOptions = 8 }; field.Choice.AttachNode(field.Root);
                field.Choice.OnOptionSelected = selected => {
                    if (field.Binding) return;
                    var index = field.Choice.Options.IndexOf(selected);
                    if (index >= 0 && index < (field.Row.Choices?.Count ?? 0)) Send(field.Row.Key, field.Row.Choices![index].Id);
                }; break;
            case NativeFormKind.Input: case NativeFormKind.Multiline:
                field.Input = row.Kind == NativeFormKind.Multiline ? new MacroInput() : new TextInputNode { MaxCharacters = 180, MaxBytes = 720, ShowLimitText = false };
                field.Input.CurrentTextNode.FontSize = 13; field.Input.AttachNode(field.Root);
                field.Input.OnInputReceived = text => { if (!field.Binding) dispatch(new(field.Row.Key, text.ToString())); }; break;
            case NativeFormKind.CheckList:
                field.List = new ScrollingNode<ResNode> { AutoHideScrollBar = true, ScrollSpeed = 30 }; field.List.AttachNode(field.Root); break;
            case NativeFormKind.Collection:
                field.DropZone = new NativeTrainText { IsEnabled = false }; field.DropZone.AttachNode(field.Root); break;
        }
        fields.Add(poolKey, field); return field;
    }

    private float Render(Field field, float width)
    {
        var row = field.Row; var choices = row.Choices ?? Array.Empty<NativeFormChoice>();
        field.Binding = true; field.Used = true;
        field.Label.String = row.Label; field.Label.TextTooltip = row.Tooltip.Length > 0 ? row.Tooltip : row.Label;
        field.Label.IsVisible = row.Label.Length > 0 && row.Kind != NativeFormKind.Check;
        Place(field.Label, 2, 0, width, 24); field.Label.IsVisible = row.Label.Length > 0 && row.Kind != NativeFormKind.Check;
        float height = 28;
        switch (row.Kind)
        {
            case NativeFormKind.Label: case NativeFormKind.Heading:
                var lines = row.Label.Split('\n').Length;
                height = Math.Clamp(lines * 18 + 6, 28, 70);
                Place(field.Label, 2, 0, width, height);
                field.Label.AddTextFlags(TextFlags.MultiLine);
                if (row.Kind == NativeFormKind.Heading) field.Label.TextColor = Accent;
                break;
            case NativeFormKind.Check:
                Place(field.Check!, 0, 0, width); field.Check!.String = row.Label; field.Check.IsEnabled = row.Enabled;
                field.Check.TextTooltip = row.Tooltip; field.Check.IsChecked = row.Value == "True"; break;
            case NativeFormKind.Number:
                Place(field.Label, 2, 0, width - 120); Place(field.Number!, width - 112, 0, 110);
                field.Number!.Min = row.Min; field.Number.Max = row.Max; field.Number.IsEnabled = row.Enabled;
                if (int.TryParse(row.Value, out var number) && field.Number.Value != number) field.Number.Value = number;
                break;
            case NativeFormKind.Choice:
                Place(field.Label, 2, 0, 104); Place(field.Choice!, 110, 0, width - 110);
                var options = choices.Select(c => c.Label).ToList();
                if (!field.Choice!.Options.SequenceEqual(options)) field.Choice.Options = options;
                var selected = choices.ToList().FindIndex(c => c.Id == row.Value);
                if (selected >= 0 && field.Choice.SelectedIndex != selected) field.Choice.SelectedIndex = selected;
                field.Choice.IsEnabled = row.Enabled; break;
            case NativeFormKind.Input: case NativeFormKind.Multiline:
                var multi = row.Kind == NativeFormKind.Multiline;
                Place(field.Label, 2, 0, multi ? width : 90, multi ? 22 : 28);
                Place(field.Input!, multi ? 0 : 96, multi ? 24 : 0, multi ? width : width - 96, multi ? 120 : 28);
                if (!field.Input!.IsFocused && field.Input.String.ToString() != row.Value) field.Input.String = row.Value;
                field.Input.IsEnabled = row.Enabled; field.Input.TextTooltip = row.Tooltip;
                height = multi ? 150 : 28; break;
            case NativeFormKind.CheckList:
                var columns = width >= 440 ? 2 : 1;
                var listHeight = Math.Clamp((choices.Count + columns - 1) / columns * 30 + 8, 64, 176);
                Place(field.List!, 0, 24, width, listHeight);
                while (field.Checks.Count < choices.Count)
                {
                    var check = new CheckboxNode { DisableAutoResize = true }; check.Label.FontSize = 13;
                    check.AttachNode(field.List!.ContentNode); field.Checks.Add(check);
                    unsafe { check.AddLinkedEvent(field.List.ScrollBarNode, 5, AtkEventType.MouseWheel); }
                }
                for (int i = 0; i < field.Checks.Count; i++)
                {
                    var check = field.Checks[i]; check.IsVisible = i < choices.Count; if (i >= choices.Count) continue;
                    var choice = choices[i]; Place(check, i % columns * width / columns + 2, i / columns * 30 + 2, width / columns - 12);
                    check.String = choice.Label; check.IsEnabled = row.Enabled && choice.Enabled; check.IsChecked = choice.Selected;
                    check.TextTooltip = choice.Tooltip.Length == 0 ? choice.Label : choice.Tooltip;
                    check.OnClick = _ => { if (!field.Binding) Send(field.Row.Key, choice.Id); };
                }
                field.List!.ContentNode.Size = new(width - 12, MathF.Max(listHeight, ((choices.Count + columns - 1) / columns) * 30 + 8));
                field.List.RecalculateSizes(); height = listHeight + 30; break;
            case NativeFormKind.Buttons: case NativeFormKind.Collection: case NativeFormKind.Pages:
                var list = choices.ToList(); if (row.Kind == NativeFormKind.Pages) list.Add(new("+", "+"));
                while (field.Buttons.Count < list.Count) field.Buttons.Add(Button(field.Root));
                float x = 0, y = row.Label.Length > 0 ? 24 : 0;
                for (int i = 0; i < field.Buttons.Count; i++)
                {
                    var button = field.Buttons[i]; button.IsVisible = i < list.Count; if (i >= list.Count) continue;
                    var choice = list[i]; var macro = row.Kind is NativeFormKind.Collection or NativeFormKind.Pages;
                    var w = row.Kind == NativeFormKind.Collection ? Math.Min(176, (width - 8) / Math.Max(1, (int)(width / 176))) : Math.Min(ButtonWidth(choice.Label), width);
                    if (x > 0 && x + w > width) { x = 0; y += 32; }
                    Place(button, x, y, w); button.String = choice.Label; button.IsChecked = choice.Selected;
                    button.IsEnabled = row.Enabled && choice.Enabled; button.TextTooltip = choice.Tooltip;
                    button.OnClick = () => Send(row.Kind == NativeFormKind.Collection ? "send" : row.Kind == NativeFormKind.Pages ? choice.Id == "+" ? "add" : "pages" : choice.Id, choice.Id);
                    if (macro && choice.Id != "+")
                    {
                        dragTargets.Add((button, choice.Id, row.Kind == NativeFormKind.Collection));
                        unsafe {
                            button.RemoveEvent(AtkEventType.MouseDown);
                            button.AddEvent(AtkEventType.MouseDown, (_, _, _, _, data) => {
                                if (data == null) return;
                                var mouse = new Vector2(data->MouseData.PosX, data->MouseData.PosY);
                                if (data->MouseData.ButtonId == 1) OpenMenu(mouse, choice.Id, row.Kind == NativeFormKind.Collection);
                                else if (data->MouseData.ButtonId == 0) BeginPointer(button, choice.Id, mouse, false);
                            });
                        }
                    }
                    x += w + 4;
                }
                height = y + (list.Count > 0 ? 32 : 0);
                if (field.DropZone != null)
                {
                    Place(field.DropZone, MathF.Max(0, width - 160), 0, MathF.Min(160, width), 22);
                    field.DropZone.String = "+ 拖入宏标签"; field.DropZone.LabelNode.FontSize = 12;
                    field.DropZone.Paint(ColorHelper.GetColor(8), Accent); height = MathF.Max(height, 30);
                    field.Label.Width = MathF.Max(50, width - 166);
                }
                break;
        }
        field.Root.Size = new(width, MathF.Max(28, height)); field.Binding = false; return MathF.Max(28, height);
    }

    private void Layout()
    {
        if (!setup || scroll == null || body == null) return;
        body.Position = ContentStartPosition;
        var width = MathF.Max(330, Size.X - ContentStartPosition.X * 2);
        while (toolbar.Count < snapshot.Toolbar.Count) toolbar.Add(Button(body));
        float x = 0, toolbarY = 0;
        for (int i = 0; i < toolbar.Count; i++)
        {
            var button = toolbar[i]; button.IsVisible = i < snapshot.Toolbar.Count; if (i >= snapshot.Toolbar.Count) continue;
            var entry = snapshot.Toolbar[i]; var w = ButtonWidth(entry.Label);
            if (x > 0 && x + w > width) { x = 0; toolbarY += 32; }
            Place(button, x, toolbarY, w); button.String = entry.Label; button.IsChecked = entry.Selected; button.IsEnabled = entry.Enabled; button.OnClick = () => Send(entry.Id); x += w + 4;
        }
        var top = toolbarY + 36;
        var viewport = MathF.Max(100, Size.Y - ContentStartPosition.Y - top - 46);
        Place(scroll, 0, top, width, viewport);
        foreach (var field in fields.Values) field.Used = false;
        dragTargets.Clear(); float y = 0;
        foreach (var row in snapshot.Rows)
        {
            // Reuse the two native editor controls across pages instead of retaining
            // a large native TextInput tree for every macro ever visited.
            var poolKey = row.Kind is NativeFormKind.Input or NativeFormKind.Multiline ? "editor:" + row.Kind : row.Key;
            var field = fields.GetValueOrDefault(poolKey) ?? Create(row, poolKey);
            if (field.Row.Key != row.Key) field.Input?.ClearFocus();
            field.Row = row;
            Place(field.Root, 0, y, width - 14); y += Render(field, width - 14) + 4;
        }
        foreach (var field in fields.Values.Where(f => !f.Used))
        { field.Input?.ClearFocus(); field.Root.IsVisible = false; }
        scroll.ContentNode.Size = new(width - 14, MathF.Max(viewport, y)); scroll.RecalculateSizes();
        Place(status!, 0, top + viewport + 4, width - 14, 36);
        status!.String = snapshot.Status; status.TextTooltip = snapshot.Status;
        resize!.Position = Size - new Vector2(23, 23); lastSize = Size;
        if (menuBackground != null) menuBackground.Color = Light ? new(.9f, .89f, .86f, 1) : new(.07f, .10f, .16f, 1);
    }

    private unsafe void OpenMenu(Vector2 mouse, string id, bool collection)
    {
        EndPointer();
        if (menu == null)
        {
            menu = new ResNode(); menu.AttachNode(this); menuBackground = new ColorImageNode(); menuBackground.AttachNode(menu);
            for (int i = 0; i < 3; i++) menuButtons.Add(Button(menu));
        }
        var choices = new List<(string Label, string Key)> { ("编辑", "edit"), (collection ? "移出宏集合" : "加入宏集合", collection ? "unpin" : "pin") };
        if (id is not ("a" or "s")) choices.Add(("删除自定义宏", "delete"));
        var scale = InternalAddon == null ? 1 : MathF.Max(.1f, InternalAddon->Scale);
        menu.IsVisible = true; closeMenu = false; menuOpenedAt = Environment.TickCount64;
        menu.Size = new(176, choices.Count * 32 + 12);
        menu.Position = Vector2.Clamp((mouse - RootNode.ScreenPosition) / scale, new(8, 38), Vector2.Max(new(8, 38), Size - menu.Size - new Vector2(8)));
        menuBackground!.Size = menu.Size; menuBackground.Color = Light ? new(.9f, .89f, .86f, 1) : new(.07f, .10f, .16f, 1);
        for (int i = 0; i < menuButtons.Count; i++)
        {
            var button = menuButtons[i]; button.IsVisible = i < choices.Count; if (i >= choices.Count) continue;
            var choice = choices[i]; Place(button, 6, 6 + i * 32, 164); button.String = choice.Label;
            button.OnClick = () => { HideMenu(); dispatch(new(choice.Key, id)); };
        }
        if (menuPointer != null) return;
        menuPointer = new ViewportEventListener((_, _, _, _, data) => {
            if (data == null || menu == null || !menu.IsVisible || Environment.TickCount64 - menuOpenedAt < 150) return;
            if (!NativeDragGeometry.Contains(new(data->MouseData.PosX, data->MouseData.PosY), menu.ScreenPosition, menu.Size, InternalAddon == null ? 1 : InternalAddon->Scale)) closeMenu = true;
        }); menuPointer.AddEvent(AtkEventType.MouseDown, menu);
    }
    private void HideMenu() { if (menu != null) menu.IsVisible = false; closeMenu = true; }

    private void BeginPointer(NodeBase node, string id, Vector2 mouse, bool isResize)
    {
        HideMenu(); EndPointer(); dragId = id; resizing = isResize;
        startMouse = mousePosition = mouse; startSize = Size; lastScroll = Environment.TickCount64; scrollRemainder = 0;
        unsafe { pointer = new ViewportEventListener(OnPointer); pointer.AddEvent(AtkEventType.MouseMove, node); pointer.AddEvent(AtkEventType.MouseUp, node); }
    }
    private unsafe void OnPointer(AtkEventListener* listener, AtkEventType type, int param, AtkEvent* evt, AtkEventData* data)
    {
        if (data == null || dragDone) return;
        mousePosition = new(data->MouseData.PosX, data->MouseData.PosY); var delta = mousePosition - startMouse;
        dragging |= delta.LengthSquared() >= 36;
        if (dragging) suppressClickUntil = Environment.TickCount64 + 300;
        var scale = InternalAddon == null ? 1 : MathF.Max(.1f, InternalAddon->Scale);
        if (type == AtkEventType.MouseUp) dragDone = true;
        if (resizing)
        { if (type == AtkEventType.MouseMove) { Size = Vector2.Clamp(startSize + delta / scale, new(380, 380), new(1600, 1600)); Layout(); } else if (dragDone) saveSize(Size); return; }
        if (!dragging || scroll == null) return;
        cue!.IsVisible = false;
        if (!NativeDragGeometry.Contains(mousePosition, scroll.ScreenPosition, scroll.Size, scale)) return;
        foreach (var (node, id, collection) in dragTargets.Where(t => t.Collection))
        {
            if (!NativeDragGeometry.Contains(mousePosition, node.ScreenPosition, node.Size, scale)) continue;
            var after = NativeDragGeometry.After(mousePosition, node.ScreenPosition, node.Size, scale, false);
            if (id == dragId) return;
            if (dragDone) dispatch(new("pin", dragId, id, after));
            else { cue.Position = (node.ScreenPosition - RootNode.ScreenPosition) / scale + new Vector2(after ? node.Width : 0, 0); cue.Size = new(2, 28); cue.Color = Light ? new(.03f, .38f, .42f, .85f) : new(.3f, .85f, .9f, .85f); cue.IsVisible = true; }
            return;
        }
        var zone = fields.GetValueOrDefault("collection")?.DropZone;
        if (zone != null && NativeDragGeometry.Contains(mousePosition, zone.ScreenPosition, zone.Size, scale))
        {
            if (dragDone) dispatch(new("pin", dragId));
            else { cue.Position = (zone.ScreenPosition - RootNode.ScreenPosition) / scale + new Vector2(0, zone.Height); cue.Size = new(zone.Width, 2); cue.Color = new(.15f, .7f, .8f, .85f); cue.IsVisible = true; }
        }
    }
    private void EndPointer() { pointer?.Dispose(); pointer = null; dragDone = dragging = resizing = false; if (cue != null) cue.IsVisible = false; }
    protected override unsafe void OnUpdate(AtkUnitBase* addon)
    {
        base.OnUpdate(addon); if (dragDone) EndPointer(); if (setup && Size != lastSize) Layout();
        if (closeMenu) { if (menu != null) menu.IsVisible = false; menuPointer?.Dispose(); menuPointer = null; closeMenu = false; }
        if (pointer != null && dragging && !resizing && scroll != null && InternalAddon != null)
        {
            var now = Environment.TickCount64; var direction = NativeDragGeometry.ScrollDirection(mousePosition, scroll.ScreenPosition, scroll.Size, InternalAddon->Scale);
            scrollRemainder += direction * Math.Clamp(now - lastScroll, 0, 50) * .3f; lastScroll = now;
            var step = (int)scrollRemainder;
            if (step != 0) { scroll.ScrollBarNode.ScrollPosition = Math.Clamp(scroll.ScrollBarNode.ScrollPosition + step, 0, scroll.ScrollBarNode.ScrollMaxPosition); scrollRemainder -= step; }
        }
    }
    protected override unsafe void OnHide(AtkUnitBase* addon)
    {
        EndPointer(); menuPointer?.Dispose(); menuPointer = null;
        foreach (var field in fields.Values) field.Input?.ClearFocus();
        saveSize(Size); if (!suppressClose) dispatch(new("close")); base.OnHide(addon);
    }
    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    {
        setup = false; EndPointer(); menuPointer?.Dispose(); menuPointer = null;
        fields.Clear(); toolbar.Clear(); dragTargets.Clear(); menuButtons.Clear(); body = null; scroll = null; menu = null; menuBackground = null; cue = null;
        base.OnFinalize(addon);
    }
}
