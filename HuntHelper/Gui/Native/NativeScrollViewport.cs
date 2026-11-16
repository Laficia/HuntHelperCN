using KamiToolKit.Nodes;
using KamiToolKit.Nodes.Simplified;

namespace HuntHelper.Gui.Native;

/// <summary>
/// Keeps native clipping inside a component's renderer instead of allowing
/// nested ResNode clips to affect fixed controls or subsequently drawn addons.
/// </summary>
internal sealed class NativeScrollViewport : SimpleComponentNode
{
    private readonly ScrollingNode<ResNode> scroll;

    internal NativeScrollViewport()
    {
        scroll = new ScrollingNode<ResNode>();
        scroll.AttachNode(this);
    }

    internal ResNode ContentNode => scroll.ContentNode;
    internal ScrollBarNode ScrollBarNode => scroll.ScrollBarNode;
    internal bool AutoHideScrollBar
    {
        get => scroll.AutoHideScrollBar;
        set => scroll.AutoHideScrollBar = value;
    }
    internal int ScrollSpeed
    {
        get => scroll.ScrollSpeed;
        set => scroll.ScrollSpeed = value;
    }
    internal void RecalculateSizes()
    {
        // Keep the clipping rectangle in sync even if the owner requests a
        // recalculation without triggering another component resize callback.
        scroll.Size = Size;
        scroll.RecalculateSizes();
    }

    protected override void OnSizeChanged()
    {
        base.OnSizeChanged();
        // The component base can resize while this constructor is still running.
        if (scroll != null) scroll.Size = Size;
    }
}
