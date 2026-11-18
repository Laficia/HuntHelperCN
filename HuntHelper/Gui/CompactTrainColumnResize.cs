using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HuntHelper.Gui;

internal sealed class CompactTrainColumnResize(float defaultWidth = 120f)
{
    public const float HandleWidth = 12f;
    public const float MinimumWidth = 80f;
    private const double HoldSeconds = .35;
    private double? pressedAt;
    private float startMouseX;
    private float? previewWidth;

    public float Width(float savedWidth) => previewWidth ?? Clamp(savedWidth);

    private float Clamp(float width) => float.IsFinite(width) ? Math.Clamp(width, MinimumWidth, 420f) : defaultWidth;

    public bool Draw(float scale, ref float savedWidth, string columnName = "怪物")
    {
        ImGui.InvisibleButton("##resize-" + columnName, new Vector2(HandleWidth * scale, ImGui.GetTextLineHeight()));
        if (ImGui.IsItemActivated())
        {
            pressedAt = ImGui.GetTime();
            startMouseX = ImGui.GetMousePos().X;
        }

        bool changed = false;
        if (ImGui.IsKeyPressed(ImGuiKey.Escape)) Cancel();
        else if (pressedAt is { } since)
        {
            if (ImGui.IsItemActive() && ImGui.GetTime() - since >= HoldSeconds)
                previewWidth = Clamp(Clamp(savedWidth) + (ImGui.GetMousePos().X - startMouseX) / scale);
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && previewWidth.HasValue)
            {
                float finalWidth = Clamp(Clamp(savedWidth) + (ImGui.GetMousePos().X - startMouseX) / scale);
                changed = finalWidth != savedWidth;
                savedWidth = finalWidth;
                Cancel();
            }
            else if (!ImGui.IsItemActive()) Cancel();
        }

        var hovered = ImGui.IsItemHovered();
        if (hovered || pressedAt.HasValue) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
        if (hovered && !pressedAt.HasValue) ImGui.SetTooltip($"长按左键并左右拖动，调整{columnName}列宽");
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var color = TrainTheme.DragColor(previewWidth.HasValue || hovered ? .9f : .65f);
        var draw = ImGui.GetWindowDrawList();
        float x = (min.X + max.X) / 2;
        draw.AddLine(new(x - 2 * scale, min.Y + 3 * scale), new(x - 2 * scale, max.Y - 3 * scale), color, scale);
        draw.AddLine(new(x + 2 * scale, min.Y + 3 * scale), new(x + 2 * scale, max.Y - 3 * scale), color, scale);
        return changed;
    }

    public void Cancel()
    {
        pressedAt = null;
        previewWidth = null;
    }
}
