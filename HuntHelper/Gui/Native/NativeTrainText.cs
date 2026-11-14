using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace HuntHelper.Gui.Native;

/// <summary>Plain native text with an independent hit area, without button textures or press timelines.</summary>
internal sealed class NativeTrainText : ResNode
{
    private readonly ColorImageNode highlight;
    internal TextNode LabelNode { get; }
    internal CollisionNode Hitbox { get; }
    internal Action? OnClick;
    internal bool IsChecked { get; set; }
    internal bool IsEnabled { get; set; } = true;
    private bool hovered;
    private float hoverAmount;
    private long lastTick = Environment.TickCount64;
    private Vector4 foreground, accent;

    internal NativeTrainText()
    {
        highlight = new ColorImageNode { Color = Vector4.Zero };
        highlight.AttachNode(this);
        LabelNode = new TextNode { FontSize = 14, AlignmentType = AlignmentType.Left, TextColorID = 8 };
        LabelNode.AddTextFlags(TextFlags.Ellipsis);
        LabelNode.AttachNode(this);
        Hitbox = new CollisionNode { ShowClickableCursor = true };
        Hitbox.AttachNode(this);
        Hitbox.AddEvent(AtkEventType.MouseOver, () => hovered = true);
        Hitbox.AddEvent(AtkEventType.MouseOut, () => hovered = false);
        unsafe { Hitbox.AddEvent(AtkEventType.MouseClick, (_, _, _, _, data) => {
            if (IsEnabled && data != null && data->MouseData.ButtonId == 0) OnClick?.Invoke();
        }); }
    }

    internal string String { set => LabelNode.String = value; }
    internal new string TextTooltip { set => Hitbox.TextTooltip = value; }

    internal void Arrange()
    {
        highlight.Size = Size;
        // Neither hover nor press modifies any position or font metric.
        LabelNode.Position = new(6, 0); LabelNode.Size = Size - new Vector2(12, 0);
        Hitbox.Size = Size; Hitbox.ShowClickableCursor = IsEnabled;
    }

    internal void Paint(Vector4 text, Vector4 emphasis)
    { foreground = text; accent = emphasis; Animate(); }

    internal void Animate()
    {
        var now = Environment.TickCount64;
        var delta = Math.Clamp(now - lastTick, 0, 50); lastTick = now;
        if (!IsVisible) hovered = false;
        var target = hovered && IsEnabled ? 1f : 0f;
        hoverAmount += (target - hoverAmount) * (1 - MathF.Exp(-delta / 65f));
        highlight.Color = accent with { W = (IsChecked ? .065f : 0) + hoverAmount * .09f };
        LabelNode.TextColor = Vector4.Lerp(foreground, accent, hoverAmount * .35f);
    }
}
