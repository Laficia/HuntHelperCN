using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Gui.Native;

internal static class CompactToolbarChecks
{
    public static void Run()
    {
        var toolbar = new CompactTrainToolbar();
        var exportWindow = new TrainExportWindow("A");
        var order = new List<uint>();
        var bounds = new Dictionary<NativeTrainAction, (Vector2 Min, Vector2 Max)>();
        var clicks = new List<NativeTrainAction>();
        var attempted = new List<TrainExportScope>();
        var copied = new List<string>();
        bool clipboardAvailable = false;
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
            if (selected == NativeTrainAction.Export) exportWindow.Open();
            if (reordered) saves++;
            ImGui.End();
            if (exportWindow.IsOpen)
                exportWindow.Draw("7A", scope =>
                {
                    attempted.Add(scope);
                    return scope == TrainExportScope.Version ? null : scope.ToString();
                }, code => { copied.Add(code); return clipboardAvailable; });
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

        Hold(NativeTrainAction.Export); Mouse(false);
        Check(!exportWindow.IsOpen, "holding the Export icon opened the dialog");
        var originalWindow = ImGui.GetCurrentContext().NavWindow;
        Aim(NativeTrainAction.Export); Mouse(true); Mouse(false); Frame(); Frame();
        Check(exportWindow.IsOpen && attempted.Count == 0, "a normal Export click did not open the separate scope dialog");
        var window = ImGui.GetCurrentContext().NavWindow;
        Check(!window.IsNull && !window.Collapsed && window != originalWindow,
            "Export dialog was not visible and focused above the train window");
        var style = ImGui.GetStyle();
        var buttonHeight = ImGui.GetFrameHeight();
        var first = window.Pos + new Vector2(style.WindowPadding.X + 20, buttonHeight + style.WindowPadding.Y + buttonHeight / 2);
        var row = new Vector2(0, buttonHeight + style.ItemSpacing.Y);
        void Click(Vector2 point) { Point(point); Mouse(true); Mouse(false); }
        Click(first);
        Check(attempted.SequenceEqual(new[] { TrainExportScope.Version }) && copied.Count == 0, "empty export attempted to replace clipboard contents");
        Click(first + row);
        Check(attempted.LastOrDefault() == TrainExportScope.Server && copied.SequenceEqual(new[] { "Server" }),
            "server export was not produced when the clipboard write failed");
        Frame();
        var retryY = window.Pos.Y + window.Size.Y - style.WindowPadding.Y - buttonHeight / 2;
        clipboardAvailable = true;
        Click(new Vector2(first.X, retryY));
        Check(copied.SequenceEqual(new[] { "Server", "Server" }), "retry did not copy the same saved code");
        Click(first + row * 2);
        Check(attempted.LastOrDefault() == TrainExportScope.AllServers && copied[^1] == "AllServers",
            "all-server export selected the wrong scope or did not copy");
        Console.WriteLine("PASS Compact Export opens a focused dialog, preserves scopes and retries the generated code after clipboard failure");
    }
}
