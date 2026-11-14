using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui.Native;

internal readonly record struct NativeMenuChoice(string Label, NativeTrainRequest Request);

internal static class NativeTrainMenus
{
    internal static IReadOnlyList<NativeMenuChoice> Row(uint world, string key) => [
        new("选择", new(NativeTrainAction.SelectOnly, world, key)),
        new("上移", new(NativeTrainAction.RowUp, world, key)),
        new("下移", new(NativeTrainAction.RowDown, world, key)),
        new("删除记录", new(NativeTrainAction.RemoveRow, world, key)),
    ];
    internal static IReadOnlyList<NativeMenuChoice> Map(uint world, string key) => [
        new("打开地图", new(NativeTrainAction.Map, world, key)),
        new("发送坐标", new(NativeTrainAction.Coordinates, world, key)),
    ];
    internal static IReadOnlyList<NativeMenuChoice> Tab(uint world, uint id, bool server) => server ? [
        new("选择", new(NativeTrainAction.World, id)),
        new("向左移动", new(NativeTrainAction.ShiftWorld, world, id.ToString())),
        new("向右移动", new(NativeTrainAction.ShiftWorld, world, id.ToString(), Value: true)),
        new("删除区服记录", new(NativeTrainAction.DeleteWorld, world, id.ToString())),
    ] : [
        new("选择", new(NativeTrainAction.Version, world, id.ToString())),
        new("向左移动", new(NativeTrainAction.ShiftVersion, world, id.ToString())),
        new("向右移动", new(NativeTrainAction.ShiftVersion, world, id.ToString(), Value: true)),
        new("清空此标签", new(NativeTrainAction.ClearVersion, world, id.ToString())),
    ];
}

internal static class NativeDragGeometry
{
    // KamiToolKit NodeBase.Bounds is local. Native mouse events are in screen pixels.
    internal static bool Contains(Vector2 pointer, Vector2 screenPosition, Vector2 localSize, float scale) =>
        pointer.X >= screenPosition.X && pointer.Y >= screenPosition.Y &&
        pointer.X <= screenPosition.X + localSize.X * scale && pointer.Y <= screenPosition.Y + localSize.Y * scale;
    internal static bool After(Vector2 pointer, Vector2 screenPosition, Vector2 localSize, float scale, bool vertical) =>
        vertical ? pointer.Y > screenPosition.Y + localSize.Y * scale / 2 : pointer.X > screenPosition.X + localSize.X * scale / 2;
    internal static int ScrollDirection(Vector2 pointer, Vector2 screenPosition, Vector2 localSize, float scale)
    {
        if (!Contains(pointer, screenPosition, localSize, scale)) return 0;
        if (pointer.Y < screenPosition.Y + 20 * scale) return -1;
        if (pointer.Y > screenPosition.Y + (localSize.Y - 20) * scale) return 1;
        return 0;
    }
}
