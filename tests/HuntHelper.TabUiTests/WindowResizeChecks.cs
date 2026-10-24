using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;

internal static class WindowResizeChecks
{
    private static Vector2 _size = new(680, 350);
    private static Vector2 _position = new(8, 8);
    private static Vector2 _resetMin, _resetMax;
    private static float _scale = 1;
    private static bool _visible = true;

    public static void Run()
    {
        Frame(); Frame();
        DragCorner(new Vector2(-200, -110));
        Require(_size.X < 620 && _size.Y < 300, $"Window cannot shrink below the old limits: {_size}");
        Console.WriteLine("PASS Native resize corner shrinks the train window below its old minimum");
        var smaller = _size;
        DragCorner(new Vector2(100, 60));
        Require(_size.X > smaller.X + 80 && _size.Y > smaller.Y + 40, "Corner cannot enlarge the window");
        Console.WriteLine("PASS Native resize corner enlarges both window dimensions");
        DragCorner(new Vector2(20, 30) - (_position + _size - new Vector2(4)));
        Require(Vector2.Distance(_size, new Vector2(280, 160)) < 3, $"Wrong compact minimum: {_size}");
        Console.WriteLine("PASS Compact minimum preserves usable width and height");

        var io = ImGui.GetIO();
        Frame(); // Let the new compact layout settle before locating its controls.
        var button = (_resetMin + _resetMax) / 2;
        io.AddMousePosEvent(button.X, button.Y); Frame();
        io.AddMouseButtonEvent(0, true); Frame();
        io.AddMouseButtonEvent(0, false); Frame(); Frame();
        Require(Vector2.Distance(_size, new Vector2(520, 300)) < 3, $"Default-size control failed: {_size}");
        Console.WriteLine("PASS Real Restore default size control recovers a compact window");

        _scale = 1.5f; Frame(); Frame();
        Require(_size.X >= 360 && _size.Y >= 240 && _size.X < 930 && _size.Y <= 376,
            $"Scaled size limits exceed the viewport or retain the old wide minimum: {_size}");
        Console.WriteLine("PASS Size limits adapt to UI scale and stay within the viewport");
        var original=ImGui.GetStyle().WindowMinSize;
        ImGui.GetStyle().WindowMinSize=new(420,180);
        DragCorner(new Vector2(-900,-900));Frame();
        Require(_size.X<420 && ImGui.GetStyle().WindowMinSize.X==420,$"Host minimum or UI scaling blocks compact width: {_size}");
        ImGui.GetStyle().WindowMinSize=original;
        Console.WriteLine("PASS Native window shrinks below 420 despite a host minimum and 150% UI scale");
    }

    private static void Frame()
    {
        ImGui.NewFrame();
        if (HuntTrainWindowLayout.Begin("TrainWindowResizeTest", ref _visible, _size, _position, _scale))
        {
            HuntTrainWindowLayout.DrawSizeControls(_scale);
            _resetMin = ImGui.GetItemRectMin(); _resetMax = ImGui.GetItemRectMax();
            _size = ImGui.GetWindowSize(); _position = ImGui.GetWindowPos();
        }
        HuntTrainWindowLayout.End();
        ImGui.Render();
    }

    private static void DragCorner(Vector2 delta)
    {
        var io = ImGui.GetIO();
        var corner = _position + _size - new Vector2(4);
        io.AddMousePosEvent(corner.X, corner.Y); Frame();
        io.AddMouseButtonEvent(0, true); Frame();
        io.AddMousePosEvent(corner.X + delta.X, corner.Y + delta.Y); Frame();
        io.AddMouseButtonEvent(0, false); Frame();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
