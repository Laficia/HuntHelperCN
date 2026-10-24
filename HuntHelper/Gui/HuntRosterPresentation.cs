using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace HuntHelper.Gui;

/// <summary>Local styles for the roster; never changes Dalamud's global theme.</summary>
public static class HuntRosterPresentation
{
    public static readonly Vector4 FoundColor = new(.39f, .84f, .67f, 1);
    public static readonly Vector4 MissingColor = new(.96f, .42f, .46f, 1);

    public static void Summary(string world, int version, int recorded, string rankLabel = "A")
    {
        ImGui.TextColored(new Vector4(.84f, .91f, 1, 1), $"{world}  /  {version}{rankLabel}");
        ImGui.TextColored(FoundColor, $"已录入 {recorded}");
        ImGui.Spacing();
    }

    public static void Status(bool found, bool dead, float scale)
    {
        var text = found ? dead ? "已死亡" : "已发现" : "未发现";
        var color = found ? dead ? new Vector4(.6f, .65f, .72f, 1) : FoundColor : MissingColor;
        var background = new Vector4(color.X, color.Y, color.Z, .13f);
        var padding = new Vector2(5, 2) * scale;
        var position = ImGui.GetCursorScreenPos();
        var size = ImGui.CalcTextSize(text) + padding * 2;
        ImGui.Dummy(size);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(position, position + size, ImGui.GetColorU32(background), 4 * scale);
        draw.AddText(position + padding, ImGui.GetColorU32(color), text);
    }
}
