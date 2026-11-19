using System.Numerics;
using KamiToolKit.Classes;
using KamiToolKit.Nodes;

namespace HuntHelper.Gui.Native;

internal abstract partial class NativePersistentAddon
{
    private TextButtonNode? closeLockButton;
    private TextureButtonNode? titleCloseButton;
    private ColorImageNode[]? lockIcon;

    protected void AddCloseLockButton()
    {
        if (WindowNode is not WindowNode window) return;
        // The standard window component handles its own close node and can call
        // the game's Hide directly. Use a separate node that honors Close().
        window.ShowCloseButton = false;
        window.CloseButtonNode.IsEnabled = false;
        window.CloseButtonNode.CollisionNode.IsVisible = false;
        titleCloseButton = new TextureButtonNode
        {
            Size = window.CloseButtonNode.Size,
            TexturePath = "ui/uld/WindowA_Button.tex",
            TextureCoordinates = Vector2.Zero,
            TextureSize = new(28, 28),
            OnClick = Close,
            TextTooltip = "关闭窗口"
        };
        titleCloseButton.AttachNode(window.HeaderContainerNode);
        closeLockButton = new TextButtonNode { Size = new(26, 22), OnClick = () =>
        {
            ToggleCloseLock();
            ArrangeCloseLock();
        }};
        closeLockButton.AttachNode(window.HeaderContainerNode);
        lockIcon = new ColorImageNode[4];
        for (int i = 0; i < lockIcon.Length; i++)
        {
            lockIcon[i] = new ColorImageNode();
            lockIcon[i].AttachNode(closeLockButton);
        }
        ArrangeCloseLock();
    }

    partial void ArrangeCloseLock()
    {
        if (closeLockButton == null || titleCloseButton == null || lockIcon == null || WindowNode is not WindowNode window) return;
        closeLockButton.Position = new(window.Width - 60, window.CloseButtonNode.Y + 2);
        titleCloseButton.Position = window.CloseButtonNode.Position;
        titleCloseButton.IsEnabled = !CloseLocked;
        titleCloseButton.IsVisible = !CloseLocked;
        closeLockButton.TextTooltip = CloseLocked ? "已锁定关闭，单击解锁" : "锁定窗口，防止误关";
        var color = ColorHelper.GetColor(8);
        Paint(lockIcon[0], new(8, 10), new(10, 7), color);
        Paint(lockIcon[1], new(10, 5), new(6, 1), color);
        Paint(lockIcon[2], new(10, 5), new(1, CloseLocked ? 5 : 3), color);
        Paint(lockIcon[3], new(15, 5), new(1, 5), color);
    }

    private static void Paint(ColorImageNode node, Vector2 position, Vector2 size, Vector4 color)
    {
        node.Position = position;
        node.Size = size;
        node.Color = color;
    }

    partial void ClearCloseLockButton()
    {
        closeLockButton = null;
        titleCloseButton = null;
        lockIcon = null;
    }
}
