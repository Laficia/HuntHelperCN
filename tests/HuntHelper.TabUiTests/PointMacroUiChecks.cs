using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Utilities;

internal static class PointMacroUiChecks
{
    private static readonly HuntPointMacroSettings Settings = new();
    private static int _saves, _cancels;
    private static readonly HuntPointMacroUI Editor = new(Settings, () => _saves++, () => false, () => "", () => _cancels++);
    public static void Run()
    {
        Editor.Open(); Frame(); Frame();
        if (_saves != 0 || _cancels != 0) throw new Exception("Opening macro editor mutated state");
        Console.WriteLine("PASS Native macro editor opens without saving or sending");
        Editor.Open("S"); Frame(); Frame();
        if (Settings.S != new HuntPointMacroSettings().S) throw new Exception("Tab switch altered S preset");
        Console.WriteLine("PASS Opening S macro preset keeps the saved A and S templates independent");
    }
    private static void Frame()
    {
        ImGui.NewFrame(); ImGui.SetNextWindowPos(new(20, 20)); ImGui.SetNextWindowSize(new(700, 430));
        Editor.Draw(); ImGui.Render();
    }
    private static void Click(Vector2 p)
    {
        var io = ImGui.GetIO(); io.AddMousePosEvent(p.X, p.Y); io.AddMouseButtonEvent(0, true); Frame();
        io.AddMouseButtonEvent(0, false); Frame();
    }
    public static void RenderPreview()
    {
        Editor.Open();
        RosterPreviewRenderer.RenderWidget("obj/previews/point-macros/a-preset.png", 760, 470, Frame);
        Editor.Open("S");
        RosterPreviewRenderer.RenderWidget("obj/previews/point-macros/s-preset.png", 760, 470, Frame);
    }
}
