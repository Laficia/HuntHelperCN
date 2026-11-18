using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;

internal static class TrainTabStripChecks
{
    private readonly record struct Strip(float Height, float ButtonHeight, float ScrollX, float MaxX, float MaxY);

    public static void Run()
    {
        string[] servers = ["Server One (7)", "Server Two (12)", "Server Three (11)"];
        string[] versions = ["All (7)", "7A (7)"];
        Strip server = default, version = default;
        float width = 610;
        bool scrollServer = false;
        const float scale = 1.17f;
        ImGui.GetIO().AddMousePosEvent(-100, -100);

        void Frame()
        {
            ImGui.NewFrame();
            ImGui.SetNextWindowPos(new(12, 20));
            ImGui.SetNextWindowSize(new(width, 220));
            ImGui.Begin("CompactTabsLayout", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
            server = Draw("ServerStrip", servers, scale, scrollServer);
            version = Draw("VersionStrip", versions, scale, false);
            ImGui.End();
            ImGui.Render();
        }

        Frame(); Frame();
        Check(server.MaxX == 0 && version.MaxX == 0 && server.Height == server.ButtonHeight && version.Height == version.ButtonHeight,
            "tabs that fit reserve exactly one button-height each");

        width = 220;
        Frame(); Frame(); Frame();
        Check(server.MaxX > 0 && server.MaxY == 0 && server.Height > server.ButtonHeight &&
            version.MaxX == 0 && version.MaxY == 0 && version.Height == version.ButtonHeight,
            "only the overflowing row gets a horizontal scrollbar");
        scrollServer = true;
        Frame(); Frame();
        Check(server.ScrollX > 0 && version.ScrollX == 0, "rows have independent horizontal scroll offsets");

        scrollServer = false;
        width = 610;
        Frame(); Frame(); Frame();
        Check(server.MaxX == 0 && server.ScrollX == 0 && server.Height == server.ButtonHeight,
            "widening resets the horizontal offset and removes its extra height");
        Console.WriteLine("PASS Compact server and version strips allocate horizontal scroll only on overflow");
    }

    private static Strip Draw(string id, IReadOnlyList<string> labels, float scale, bool scroll)
    {
        Strip strip = default;
        if (TrainTabStrip.Begin(id, labels, scale, out var buttonHeight))
        {
            for (int i = 0; i < labels.Count; i++)
            {
                if (i != 0) ImGui.SameLine();
                ImGui.Button(labels[i], new(TrainTabStrip.ButtonWidth(labels[i]), buttonHeight));
            }
            if (scroll) ImGui.SetScrollX(ImGui.GetScrollMaxX());
            strip = new(ImGui.GetWindowSize().Y, buttonHeight, ImGui.GetScrollX(), ImGui.GetScrollMaxX(), ImGui.GetScrollMaxY());
        }
        TrainTabStrip.End();
        return strip;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("Tab strip: " + message);
    }
}
