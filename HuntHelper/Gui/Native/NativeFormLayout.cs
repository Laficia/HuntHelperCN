using System;
using System.Numerics;

namespace HuntHelper.Gui.Native;

internal readonly record struct NativeFormBounds(Vector2 Body, float ScrollTop, float ScrollHeight, float FooterY);

internal static class NativeFormLayout
{
    internal static NativeFormBounds Arrange(Vector2 window, Vector2 start, int toolbarRows)
    {
        var body = new Vector2(MathF.Max(16, window.X - start.X * 2), MathF.Max(80, window.Y - start.Y - 16));
        var top = toolbarRows == 0 ? 0 : toolbarRows * 32 + 4;
        var height = MathF.Max(40, body.Y - top - 40);
        return new(body, top, height, top + height + 4);
    }
}
