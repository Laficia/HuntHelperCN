using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HuntHelper.Gui;

internal static class TrainTabStrip
{
    public static bool Begin(string id, IEnumerable<string> labels, float scale, out float buttonHeight)
    {
        var scrollbar = 8 * scale;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(9, 4) * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarSize, scrollbar);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);

        var gap = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = -gap;
        foreach (var label in labels) totalWidth += ButtonWidth(label) + gap;
        var overflow = totalWidth > MathF.Floor(ImGui.GetContentRegionAvail().X);
        buttonHeight = MathF.Ceiling(Math.Max(27 * scale, ImGui.GetFrameHeight()));
        var height = buttonHeight + (overflow ? scrollbar + 3 * scale : 0);
        var flags = overflow ? ImGuiWindowFlags.HorizontalScrollbar : ImGuiWindowFlags.NoScrollbar;
        var visible = ImGui.BeginChild(id, new Vector2(0, height), false, flags);
        if (!overflow && ImGui.GetScrollX() != 0) ImGui.SetScrollX(0);
        return visible;
    }

    public static float ButtonWidth(string label) =>
        MathF.Ceiling(ImGui.CalcTextSize(label, hideTextAfterDoubleHash: true).X + 2 * ImGui.GetStyle().FramePadding.X);

    public static void End()
    {
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(3);
    }
}
