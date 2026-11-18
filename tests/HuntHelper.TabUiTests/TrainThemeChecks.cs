using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;

internal static unsafe class TrainThemeChecks
{
    public static void Run()
    {
        var window = new Vector4(.08f, .10f, .16f, 1);
        var text = new Vector4(.94f, .96f, .98f, 1);
        var frame = new Vector4(.14f, .22f, .33f, 1);
        var blue = new Vector4(.22f, .42f, .66f, 1);
        var orange = new Vector4(1, .62f, .1f, 1);
        ImGui.GetIO().AddMousePosEvent(-100, -100);

        void Render(Vector4 accent, Vector4 expectedText)
        {
            ImGui.NewFrame();
            ImGui.PushStyleColor(ImGuiCol.WindowBg, window);
            ImGui.PushStyleColor(ImGuiCol.Text, text);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, frame);
            ImGui.PushStyleColor(ImGuiCol.Button, accent);
            ImGui.SetNextWindowPos(new Vector2(32, 35));
            ImGui.SetNextWindowSize(new Vector2(450, 200));
            ImGui.Begin("ThemeSwitch", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
            CheckButton(TrainButtonTone.Accent, "Accent", accent, expectedText);
            CheckButton(TrainButtonTone.Neutral, "Neutral", frame, text);
            CheckButton(TrainButtonTone.Row, "Row", Vector4.Lerp(frame, accent, .25f), text);
            var drag = new Vector4(accent.X, accent.Y, accent.Z, .65f);
            Check(TrainTheme.DragColor(.65f) == ImGui.GetColorU32(drag), "drag cue uses the active theme");
            Check(TrainTheme.Color(ImGuiCol.Button) == accent && TrainTheme.Color(ImGuiCol.Text) == text,
                "drawing leaves the global theme on the stack unchanged");
            ImGui.End();
            ImGui.PopStyleColor(4);
            ImGui.Render();
        }

        Render(blue, text);
        Render(orange, window);
        Console.WriteLine("PASS Original train controls follow a live palette switch and retain contrast without changing the host theme");
    }

    private static void CheckButton(TrainButtonTone tone, string label, Vector4 background, Vector4 text)
    {
        var list = ImGui.GetWindowDrawList();
        int first = list.VtxBuffer.Size;
        new TrainTheme(tone).Button(label, new Vector2(96, 28));
        uint fill = ImGui.GetColorU32(background);
        uint foreground = ImGui.GetColorU32(text);
        bool sawFill = false, sawText = false;
        for (int i = first; i < list.VtxBuffer.Size; i++)
        {
            uint color = list.VtxBuffer.Data[i].Col;
            sawFill |= color == fill;
            sawText |= color == foreground;
        }
        Check(sawFill && sawText, $"{label} uses the expected current-theme fill and text");
    }

    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception("Train theme: " + reason);
    }
}
