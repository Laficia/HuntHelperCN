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
        Vector2.Min(new Vector2(System.Math.Min(280 * scale, 360), 160 * scale), MaximumSize(scale));

    public static Vector2 ClampSize(Vector2 size, float scale) =>
        Vector2.Clamp(size, MinimumSize(scale), MaximumSize(scale));

    public static bool Begin(string title, ref bool visible, Vector2 savedSize, Vector2 savedPosition, float scale, bool applyBounds = false)
    {
        var condition = applyBounds ? ImGuiCond.Always : ImGuiCond.FirstUseEver;
        ImGui.SetNextWindowSize(ClampSize(savedSize, scale), condition);
        ImGui.SetNextWindowSizeConstraints(MinimumSize(scale), MaximumSize(scale));
        ImGui.SetNextWindowPos(savedPosition, condition);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, MinimumSize(scale));
        return ImGui.Begin(title, ref visible, ImGuiWindowFlags.None);
    }

    public static void End()
    {
        ImGui.End();
        ImGui.PopStyleVar();
    }

    public static bool DrawSizeControls(float scale, ref Vector2 size, ref Vector2 position)
    {
        ImGui.TextUnformatted("窗口大小（也可拖动右下角）");
        var minimum = MinimumSize(scale);
        var maximum = MaximumSize(scale);
        ImGui.SetNextItemWidth(110 * scale);
        var changed = ImGui.DragFloat("宽度", ref size.X, 2 * scale, minimum.X, maximum.X, "%.0f");
        if (ImGui.GetContentRegionAvail().X >= 330 * scale) ImGui.SameLine();
        ImGui.SetNextItemWidth(110 * scale);
        changed |= ImGui.DragFloat("高度", ref size.Y, 2 * scale, minimum.Y, maximum.Y, "%.0f");
        if (ImGui.Button("恢复默认大小"))
        {
            size = new Vector2(520, 300) * scale;
            changed = true;
        }
        if (!changed) return false;
        size = ClampSize(size, scale);
        var viewport = ImGui.GetMainViewport();
        var min = viewport.WorkPos + new Vector2(8 * scale);
        var max = Vector2.Max(min, viewport.WorkPos + viewport.WorkSize - size - new Vector2(8 * scale));
        position = Vector2.Clamp(position, min, max);
        return true;
    }
}
