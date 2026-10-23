using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace HuntHelper.Gui;

public static class HuntTrainWindowLayout
{
    public static Vector2 MaximumSize(float scale)
    {
        var work = ImGui.GetMainViewport().WorkSize;
        return Vector2.Max(new Vector2(1), work - new Vector2(16 * scale));
    }

    public static Vector2 MinimumSize(float scale) =>
        Vector2.Min(new Vector2(360, 180) * scale, MaximumSize(scale));

    public static Vector2 ClampSize(Vector2 size, float scale) =>
        Vector2.Clamp(size, MinimumSize(scale), MaximumSize(scale));

    public static bool Begin(string title, ref bool visible, Vector2 savedSize, Vector2 savedPosition, float scale)
    {
        ImGui.SetNextWindowSize(ClampSize(savedSize, scale), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(MinimumSize(scale), MaximumSize(scale));
        ImGui.SetNextWindowPos(savedPosition, ImGuiCond.FirstUseEver);
        // Make the native resize corner visible even if another theme hides resize grips.
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Vector4(.35f, .60f, .85f, .60f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, new Vector4(.40f, .70f, 1, .90f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, new Vector4(.45f, .75f, 1, 1));
        return ImGui.Begin(title, ref visible, ImGuiWindowFlags.None);
    }

    public static void End()
    {
        ImGui.End();
        ImGui.PopStyleColor(3);
    }

    public static void DrawSizeControls(float scale)
    {
        ImGui.TextUnformatted("窗口大小（也可拖动右下角）");
        var size = ImGui.GetWindowSize();
        var minimum = MinimumSize(scale);
        var maximum = MaximumSize(scale);
        ImGui.SetNextItemWidth(110 * scale);
        var changed = ImGui.DragFloat("宽度", ref size.X, 2 * scale, minimum.X, maximum.X, "%.0f");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110 * scale);
        changed |= ImGui.DragFloat("高度", ref size.Y, 2 * scale, minimum.Y, maximum.Y, "%.0f");
        if (changed) ImGui.SetWindowSize(ClampSize(size, scale));
        if (ImGui.Button("恢复默认大小")) RestoreDefaultSize(scale);
    }

    public static void RestoreDefaultSize(float scale)
    {
        var size = ClampSize(new Vector2(520, 300) * scale, scale);
        ImGui.SetWindowSize(size);
        var viewport = ImGui.GetMainViewport();
        var min = viewport.WorkPos + new Vector2(8 * scale);
        var max = Vector2.Max(min, viewport.WorkPos + viewport.WorkSize - size - new Vector2(8 * scale));
        ImGui.SetWindowPos(Vector2.Clamp(ImGui.GetWindowPos(), min, max));
    }
}
