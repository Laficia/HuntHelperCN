using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Utilities;
using System.Numerics;

internal static class VersionTabUiChecks
{
    private static readonly TrainVersionTabBar Tabs=new();
    private static List<TrainVersionGroup> _groups=new(){new(7,"7A",12),new(6,"6A",12),new(5,"5A",12),new(4,"4A",12),new(null,"All",48)};
    private static int? _selected=7;
    private static List<uint> _order=new();
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
        _groups=new(){new(7,"7A",2),new(6,"6A",1),new(5,"5A",1),new(4,"4A",2),new(null,"All",6)};
        _order.Clear();_selected=7;Frame();Frame();
        Drag(7,4,true);Frame();
        if(!Tabs.VisibleTabs.Select(b=>b.Version).SequenceEqual(new int?[]{6,5,4,7,null}) || _selected!=7)
            throw new Exception("Version drag did not preserve selection and rendered order");
        Console.WriteLine("PASS Native version drag inserts after the target with server-tab feedback");
        _order=System.Text.Json.JsonSerializer.Deserialize<List<uint>>(System.Text.Json.JsonSerializer.Serialize(_order))!;
        Frame();Drag(7,6,false);Frame();
        if(!Tabs.VisibleTabs.Select(b=>b.Version).SequenceEqual(new int?[]{7,6,5,4,null}) || !ImGui.GetDragDropPayload().IsNull)
            throw new Exception("Reverse version drag or saved-order reload failed");
        Console.WriteLine("PASS Native reverse version drag survives order reload without yellow drag targets");
    }
    private static void Frame()
    {
        ImGui.NewFrame();ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new Vector2(880,200));
        ImGui.Begin("VersionTest",ImGuiWindowFlags.NoTitleBar|ImGuiWindowFlags.NoSavedSettings);
        var action=Tabs.Draw(_groups,_selected,order:_order);if(action.Changed)_selected=action.Version;
        ImGui.End();ImGui.Render();
    }
    private static void Click(int? version)
    {
        var bounds=Tabs.VisibleTabs.Single(b=>b.Version==version);var center=(bounds.Min+bounds.Max)/2;
        var io=ImGui.GetIO();io.AddMousePosEvent(center.X,center.Y);Frame();
        io.AddMouseButtonEvent(0,true);Frame();io.AddMouseButtonEvent(0,false);Frame();
    }
    private static void Drag(int source,int target,bool after)
    {
        var io=ImGui.GetIO();var b=Tabs.VisibleTabs.Single(b=>b.Version==source);var start=(b.Min+b.Max)/2;
        io.AddMousePosEvent(start.X,start.Y);Frame();io.AddMouseButtonEvent(0,true);Frame();
        io.AddMousePosEvent(start.X+12,start.Y);Frame();
        var dest=Tabs.VisibleTabs.Single(b=>b.Version==target);
        io.AddMousePosEvent(after?dest.Max.X-3:dest.Min.X+3,(dest.Min.Y+dest.Max.Y)/2);Frame();
        io.AddMouseButtonEvent(0,false);Frame();
    }
}
