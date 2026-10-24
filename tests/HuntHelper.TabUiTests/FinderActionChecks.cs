using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using System.Numerics;

internal static class FinderActionChecks
{
    private static bool _active, _recording;
    private static Vector2 _min, _max;
    private static HuntFinderAction _action;
    public static void Run()
    {
        Frame(); Frame(); Click();
        if (_action != HuntFinderAction.Start) throw new Exception("Start button failed");
        var start = (_min + _max) / 2;
        _active=true; Frame(); Frame();
        if (Vector2.Distance(start,(_min+_max)/2)>1) throw new Exception("Stop button moved away from Start");
        Click();
        if (_action != HuntFinderAction.Stop) throw new Exception("Active Stop button was disabled");
        Console.WriteLine("PASS Native finder Start switches to clickable Stop at the same position");
        _active=false;_recording=true;Frame();Click();
        if (_action != HuntFinderAction.None) throw new Exception("Recording incorrectly permits Start");
        _active=true;Frame();Click();
        if (_action != HuntFinderAction.Stop) throw new Exception("Stop incorrectly disabled by recording state");
        Console.WriteLine("PASS Native recording blocks starting but never blocks stopping an active patrol");
    }
    private static void Frame()
    {
        ImGui.NewFrame();ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new(500,200));
        ImGui.Begin("FinderActionTest",ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoTitleBar);
        _action=HuntFinderActionButton.Draw(_active,_recording);_min=ImGui.GetItemRectMin();_max=ImGui.GetItemRectMax();
        ImGui.End();ImGui.Render();
    }
    private static void Click()
    {
        var io=ImGui.GetIO();var p=(_min+_max)/2;
        io.AddMousePosEvent(p.X,p.Y);Frame();io.AddMouseButtonEvent(0,true);Frame();io.AddMouseButtonEvent(0,false);Frame();
    }
}
