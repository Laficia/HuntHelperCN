using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HuntHelper.Gui;

internal enum TrainButtonTone { Accent, Neutral, Row }

internal readonly struct TrainTheme
{
    private readonly Vector4 normal;
    private readonly Vector4 hovered;
    private readonly Vector4 active;
    private readonly Vector4 text;

    internal TrainTheme(TrainButtonTone tone)
    {
        normal = ButtonColor(tone, ImGuiCol.FrameBg, ImGuiCol.Button);
        hovered = ButtonColor(tone, ImGuiCol.FrameBgHovered, ImGuiCol.ButtonHovered);
        active = ButtonColor(tone, ImGuiCol.FrameBgActive, ImGuiCol.ButtonActive);
        text = LegibleText(normal);
    }

    internal static unsafe Vector4 Color(ImGuiCol color) => *ImGui.GetStyleColorVec4(color);

    internal static uint DragColor(float alpha)
    {
        var accent = Color(ImGuiCol.Button);
        accent.W = alpha;
        return ImGui.GetColorU32(accent);
    }

    public bool Button(string label, Vector2 size = default)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, normal);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, hovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, active);
        ImGui.PushStyleColor(ImGuiCol.Text, text);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    private static Vector4 ButtonColor(TrainButtonTone kind, ImGuiCol frame, ImGuiCol button) => kind switch
    {
        TrainButtonTone.Accent => Color(button),
        TrainButtonTone.Neutral => Color(frame),
        _ => Vector4.Lerp(Color(frame), Color(button), .25f),
    };

    private static Vector4 LegibleText(Vector4 fill)
    {
        var window = Color(ImGuiCol.WindowBg);
        var background = Vector4.Lerp(window, fill, fill.W);
        var text = Color(ImGuiCol.Text);
        var ratio = Contrast(text, background);
        if (ratio >= 4.5f) return text;

        // A bright user accent can need the theme's dark window tone for its label.
        window.W = 1;
        return Contrast(window, background) > ratio ? window : text;
    }

    private static float Contrast(Vector4 a, Vector4 b)
    {
        static float Linear(float c) => c <= .04045f ? c / 12.92f : MathF.Pow((c + .055f) / 1.055f, 2.4f);
        static float Luminance(Vector4 c) => .2126f * Linear(c.X) + .7152f * Linear(c.Y) + .0722f * Linear(c.Z);
        float first = Luminance(a), second = Luminance(b);
        return (Math.Max(first, second) + .05f) / (Math.Min(first, second) + .05f);
    }
}
