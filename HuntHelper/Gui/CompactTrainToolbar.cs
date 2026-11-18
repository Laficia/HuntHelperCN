using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui.Native;
using HuntHelper.Utilities;

namespace HuntHelper.Gui;

internal sealed class CompactTrainToolbar
{
    private static readonly uint[] DefaultOrder =
    [
        (uint)NativeTrainAction.ClearDead, (uint)NativeTrainAction.Revive, (uint)NativeTrainAction.Backups, (uint)NativeTrainAction.Recording,
        (uint)NativeTrainAction.Clear, (uint)NativeTrainAction.Export, (uint)NativeTrainAction.Import,
    ];
    private const float ButtonHeight = 28;
    private readonly TabDragGesture drag = new(grabOnHold: true);
    private readonly List<(uint Id, Vector2 Min, Vector2 Max)> bounds = new(DefaultOrder.Length);

    public static float Height(float scale) => 2 * ButtonHeight * scale + 4 * ImGui.GetStyle().ItemSpacing.Y + 2;

    public NativeTrainAction? Draw(List<uint> order, float scale, Func<NativeTrainAction, Vector2, bool> drawButton, out bool reordered)
    {
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) drag.Cancel();
        WorldTabOrder.Synchronize(order, DefaultOrder);
        bounds.Clear();
        var size = new Vector2(34, ButtonHeight) * scale;
        var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        NativeTrainAction? clicked = null;

        for (int index = 0; index < order.Count; index++)
        {
            if (index != 0 && index != 4) ImGui.SameLine();
            if (index == 3) ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), right - size.X));
            if (index == 5) ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), right - 2 * size.X - spacing));
            var id = order[index];
            var action = (NativeTrainAction)id;
            var activated = drawButton(action, size);
            drag.RegisterItem(id);
            bounds.Add((id, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
            if (activated && !drag.SuppressClick) clicked = action;
        }

        reordered = drag.Finish(order, bounds, scale);
        return clicked;
    }

    public void Cancel()
    {
        drag.Cancel();
        drag.EndFrame();
    }
}
