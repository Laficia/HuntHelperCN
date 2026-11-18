using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Gui.Native;

internal static class CompactToolbarChecks
{
    public static void Run()
    {
        var toolbar = new CompactTrainToolbar();
        var order = new List<uint>();
        var bounds = new Dictionary<NativeTrainAction, (Vector2 Min, Vector2 Max)>();
        var clicks = new List<NativeTrainAction>();
        int saves = 0;

        void Frame()
        {
            ImGui.NewFrame();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new(450, 200));
            ImGui.Begin("CompactToolbar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
            var selected = toolbar.Draw(order, 1, (action, size) =>
            {
                var clicked = ImGui.Button(action.ToString(), size);
                bounds[action] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                return clicked;
            }, out bool reordered);
            if (selected.HasValue) clicks.Add(selected.Value);
            if (reordered) saves++;
            ImGui.End();
            ImGui.Render();
        }

        void Point(Vector2 point) { ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frame(); }
        void Mouse(bool down) { ImGui.GetIO().AddMouseButtonEvent(0, down); Frame(); }
        void Aim(NativeTrainAction action, float offsetX = 0)
        {
            var (min, max) = bounds[action];
            Point((min + max) / 2 + new Vector2(offsetX, 0));
        }
        void Hold(NativeTrainAction action)
        {
            Aim(action); Mouse(true);
            for (int i = 0; i < 30; i++) Frame();
        }
        static void Check(bool success, string message)
        {
            if (!success) throw new Exception("Compact toolbar: " + message);
        }

        Frame(); Frame();
        var record = bounds[NativeTrainAction.Recording];
        var import = bounds[NativeTrainAction.Import];
        Check(order.Count == 7 && record.Min.Y == bounds[NativeTrainAction.ClearDead].Min.Y &&
            record.Max.X == import.Max.X && record.Min.Y < import.Min.Y, "recording defaults to the first row on the right");
        Aim(NativeTrainAction.Recording); Mouse(true); Mouse(false);
        Check(clicks.SequenceEqual(new[] { NativeTrainAction.Recording }) && saves == 0, "short click activates exactly once");
        Hold(NativeTrainAction.Clear); Mouse(false);
        Check(clicks.Count == 1 && saves == 0, "long hold without moving never dispatches clear");

        Hold(NativeTrainAction.Recording); Aim(NativeTrainAction.Import, 8); Mouse(false); Frame();
        Check(order[^1] == (uint)NativeTrainAction.Recording && saves == 1 && clicks.Count == 1, "cross-row reorder persists without recording");
        var saved = order.ToArray();
        Hold(NativeTrainAction.Clear); Aim(NativeTrainAction.Revive, -8);
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape, true); Frame();
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape, false); Frame();
        Mouse(false); Frame();
        Check(order.SequenceEqual(saved) && clicks.Count == 1 && saves == 1, "Escape cancels a drop");

        toolbar = new CompactTrainToolbar();
        order = saved.ToList();
        Frame(); Frame();
        Check(bounds[NativeTrainAction.Recording].Min.Y > bounds[NativeTrainAction.Revive].Min.Y && saves == 1,
            "a recreated toolbar honors the saved order");
        Console.WriteLine("PASS Compact toolbar keeps original button positions, supports cross-row long-press sorting, and never executes a dragged icon");
    }
}
