using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Utilities;
using System.Numerics;

internal static class VersionTabUiChecks
{
    private static readonly TrainVersionTabBar Tabs=new();
    private static List<TrainVersionGroup> _groups=new(){new(7,"7A",12),new(6,"6A",12),new(5,"5A",12),new(4,"4A",12),new(null,"All",48)};
    private static int? _selected=7;
    public static void Run()
    {
        Frame();Frame();
        if (!Tabs.VisibleTabs.OrderBy(b=>b.Min.X).Select(b=>b.Version).SequenceEqual(new int?[]{7,6,5,4,null}))
            throw new Exception("Rendered version tab order mismatch");
        Console.WriteLine("PASS Native ImGui renders 7A, 6A, 5A and 4A in newest-first order");
        Click(6);if(_selected!=6)throw new Exception("Version click did not select 6A");
        Click(null);if(_selected!=null)throw new Exception("All click did not select all versions");
        Console.WriteLine("PASS Native version-tab clicks switch between a version and all records");
        _groups.RemoveAll(g=>g.Version==6);Frame();
        if(Tabs.VisibleTabs.Any(b=>b.Version==6))throw new Exception("Empty version tab remained");
        Console.WriteLine("PASS Removed version categories leave no ghost native tabs");
        _groups=new(){new(7,"7A",0,12),new(6,"6A",0,12),new(5,"5A",0,12),new(4,"4A",0,12)};
        _selected=7;Frame();Frame();Click(4);
        if(_selected!=4 || Tabs.VisibleTabs.Count!=4 || _groups.Any(g=>!g.DisplayLabel.EndsWith("(0/12)")))
            throw new Exception("Empty roster tabs did not remain selectable");
        Console.WriteLine("PASS Native zero-of-twelve roster tabs remain visible and selectable");
    }
    private static void Frame()
    {
        ImGui.NewFrame();ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new Vector2(880,200));
        ImGui.Begin("VersionTest",ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoSavedSettings);
        var action=Tabs.Draw(_groups,_selected);if(action.Changed)_selected=action.Version;
        ImGui.End();ImGui.Render();
    }
    private static void Click(int? version)
    {
        var bounds=Tabs.VisibleTabs.Single(b=>b.Version==version);var center=(bounds.Min+bounds.Max)/2;
        var io=ImGui.GetIO();io.AddMousePosEvent(center.X,center.Y);Frame();
        io.AddMouseButtonEvent(0,true);Frame();io.AddMouseButtonEvent(0,false);Frame();
    }
}
