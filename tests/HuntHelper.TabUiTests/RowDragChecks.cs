using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;

internal static class RowDragChecks
{
    private sealed record Row(string Name);
    private static readonly HuntRowDragGesture<Row> Gesture = new();
    private static List<Row> _rows = new() { new("Alpha"), new("Beta"), new("Gamma") };
    private static readonly Dictionary<string,(Vector2 Min,Vector2 Max)> Bounds = new();
    private static string _scope = "101/7A";
    private static int _commits, _clicks;
    private static float _scroll;
    private static void Check(bool ok) { if (!ok) throw new Exception("Row drag assertion failed"); }
    private static void Frame()
    {
        ImGui.NewFrame(); ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new(700,300));
        ImGui.Begin("RowDragHost", ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoTitleBar);
        Gesture.Begin(_scope,_rows); Bounds.Clear();
        if(ImGui.BeginTable("Rows",2,ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,new(0,220)))
        {
            ImGui.TableSetupColumn("Monster");ImGui.TableSetupColumn("Action");ImGui.TableHeadersRow();
            foreach(var row in _rows)
            {
                ImGui.TableNextRow();ImGui.TableNextColumn();
                if(Gesture.Selectable(row,row.Name,false))_clicks++;
                Bounds[row.Name]=(ImGui.GetItemRectMin(),ImGui.GetItemRectMax());
                ImGui.TableNextColumn();ImGui.Button("Move##"+row.Name);
            }
            var result=Gesture.Finish(1);
            _scroll=ImGui.GetScrollY();
            if(result!=null){_rows=result;_commits++;}
            ImGui.EndTable();
        }
        ImGui.End();ImGui.Render();
    }
    private static void Point(Vector2 p){ImGui.GetIO().AddMousePosEvent(p.X,p.Y);Frame();}
    private static Vector2 Center(string name){var b=Bounds[name];return(b.Min+b.Max)/2;}
    private static void Mouse(bool down){ImGui.GetIO().AddMouseButtonEvent(0,down);Frame();}
    private static void Start(string name){var p=Center(name);Point(p);Mouse(true);Point(p+new Vector2(0,9));Check(Gesture.Dragging);}
    private static void Drop(string name,bool after){var b=Bounds[name];Point(new((b.Min.X+b.Max.X)/2,after?b.Max.Y-1:b.Min.Y+1));Check(ImGui.GetDragDropPayload().IsNull);Mouse(false);Frame();}
    private static void Expect(params string[] names) { if(!_rows.Select(r=>r.Name).SequenceEqual(names)) throw new Exception("Expected "+string.Join(",",names)+" got "+string.Join(",",_rows.Select(r=>r.Name))+" commits "+_commits+" clicks "+_clicks); }
    public static void Run()
    {
        Frame();Frame();Point(Center("Alpha"));Mouse(true);Mouse(false);Check(_clicks==1 && _commits==0);
        Console.WriteLine("PASS Native row click retains ordinary selection without a reorder");
        Start("Alpha");Drop("Gamma",true);Expect("Beta","Gamma","Alpha");Check(_commits==1 && _clicks==1);
        Start("Alpha");Drop("Beta",false);Expect("Alpha","Beta","Gamma");Check(_commits==2 && _clicks==1);
        Console.WriteLine("PASS Native forward and backward row drags commit once and never activate a clicked record");
        Start("Alpha");Drop("Beta",false);Expect("Alpha","Beta","Gamma");Check(_commits==2);
        Start("Beta");Point(new(750,350));Mouse(false);Expect("Alpha","Beta","Gamma");Check(_commits==2);
        Console.WriteLine("PASS Native adjacent no-op and outside drops leave record order unchanged without native yellow payloads");
        Start("Alpha");ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape,true);Frame();
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape,false);Frame();Drop("Gamma",true);Check(_commits==2 && _clicks==1);
        Console.WriteLine("PASS Escape cancels a held row drag until release");
        Start("Alpha");_scope="102/6A";Frame();Drop("Gamma",true);Check(_commits==2 && _clicks==1);
        Start("Alpha");_rows.Add(new("Delta"));Frame();Drop("Gamma",true);Check(_commits==2 && _clicks==1);
        Console.WriteLine("PASS Server/version switches and scan mutations cancel stale row drags");
        _rows=Enumerable.Range(0,60).Select(i=>new Row($"Row{i:00}")).ToList();Frame();Frame();
        Start("Row00");Point(new(Center("Row00").X,218));
        for(var i=0;i<90;i++)Frame();Check(_scroll>100 && _commits==2);
        Point(new(750,350));Mouse(false);Check(_commits==2);
        Console.WriteLine("PASS Held record drags scroll long tables without committing until a valid drop");
    }
}
