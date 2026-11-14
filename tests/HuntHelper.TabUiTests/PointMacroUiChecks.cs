using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Utilities;

internal static class PointMacroUiChecks
{
    private static HuntPointMacroSettings Settings = new();
    private static HuntPointMacroUI Editor = null!;
    private static int _saves, _cancels;
    private static bool _busy;
    private static readonly List<(string Text,string Rank)> Sent = new();
    private static float _width = 700;
    private static void Reset()
    {
        Settings = new(); _saves = 0; _cancels = 0; _busy = false; Sent.Clear(); _width = 700;
        ImGui.GetIO().DisplaySize = new(900, 760); ImGui.GetIO().AddMousePosEvent(-100, -100);
        ImGui.GetIO().AddMouseButtonEvent(0,false);
        Editor = new(Settings, () => _saves++, () => _busy, () => "", () => _cancels++,
            (text,rank) => Sent.Add((text,rank)), rank => "使用当前选中怪物：银泪湖 · 测试怪物（"+rank+"）");
        Editor.Open(); Frame(); Frame();
    }
    public static void Run()
    {
        Reset(); Check(_saves == 0 && _cancels == 0 && Sent.Count == 0);
        Console.WriteLine("PASS Native compact macro editor opens without saving or sending");
        Click("collection:a"); Check(Sent.Single() == (Settings.A,"A") && _saves == 0);
        Console.WriteLine("PASS Native macro collection click sends the saved template in its source module context");
        Editor.Open("S"); Frame(); Frame(); Click("collection:a"); Check(Sent.Last().Rank == "S");
        Console.WriteLine("PASS Opening from HHS retains HHS target context independently of the selected editor page");
        Reset(); Click("add"); Check(Settings.Custom.Count == 1 && _saves == 1 && Sent.Count == 0);
        var id=Settings.Custom.Single().Id; Frame();
        Start("tab:"+id); Drop("drop"); Check(Settings.Collection.Last()==id && _saves==2 && Sent.Count==0);
        Check(ImGui.GetDragDropPayload().IsNull);
        Console.WriteLine("PASS Native plus adds a custom page and dragging its tab pins it exactly once without sending");
        Start("collection:"+id); Drop("collection:a",false); Check(Settings.Collection.First()==id && _saves==3 && Sent.Count==0);
        Start("collection:"+id); Drop("collection:s",true); Check(Settings.Collection.Last()==id && _saves==4 && Sent.Count==0);
        Console.WriteLine("PASS Native macro collection forward and backward drags reorder saved IDs without yellow targets");
        Start("collection:"+id); Drop("drop"); Check(_saves==4);
        Start("collection:a"); Point(new(870,740)); Mouse(false); Check(_saves==4 && Sent.Count==0);
        Console.WriteLine("PASS Unchanged and outside macro drops create no save or accidental send");
        Start("collection:a"); ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape,true); Frame();
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape,false); Frame(); Drop("collection:s",true);
        Check(_saves==4 && Sent.Count==0);
        Console.WriteLine("PASS Escape cancels macro drags until mouse release");
        Click("tab:"+id); SetText("name","Countdown"); SetText("text","/sh Wait for countdown");
        Check(Settings.Custom.Single().Name!="Countdown" && Sent.Count==0);
        Click("save"); Check(Settings.Custom.Single().Name=="Countdown" && Settings.Custom.Single().Text=="/sh Wait for countdown");
        Check(Settings.A==HuntPointMacroSettings.DefaultTemplate && Settings.S==HuntPointMacroSettings.DefaultTemplate);
        Click("collection:"+id); Check(Sent.Single().Text=="/sh Wait for countdown");
        Console.WriteLine("PASS Native editing saves the named custom macro without changing A/S and sends its saved content");
        _busy=true; Click("collection:a"); Check(Sent.Count==1);
        Console.WriteLine("PASS Busy macro queues block a second collection submission");
        _busy=false; _width=320; Frame(); Frame();
        var all=Editor.ItemBounds; Check(all.Where(p=>p.Key.StartsWith("tab:")||p.Key.StartsWith("collection:")||p.Key=="add").All(p=>p.Value.Max.X<=340.1f));
        Console.WriteLine("PASS Native compact macro tiles and extensible tabs wrap inside a narrow window");
    }
    private static void Check(bool ok){if(!ok)throw new Exception("Native macro UI assertion failed; saves="+_saves+" sent="+Sent.Count);}
    private static void Frame()
    {
        ImGui.NewFrame(); ImGui.SetNextWindowPos(new(20,20)); ImGui.SetNextWindowSize(new(_width,680));
        Editor.Draw(); ImGui.SetWindowSize("预设宏##HuntPointMacros", new(_width,680)); ImGui.Render();
    }
    private static Vector2 Center(string key){var b=Editor.ItemBounds[key];return(b.Min+b.Max)/2;}
    private static void Point(Vector2 p){ImGui.GetIO().AddMousePosEvent(p.X,p.Y);Frame();}
    private static void Mouse(bool down){ImGui.GetIO().AddMouseButtonEvent(0,down);Frame();}
    private static void Click(string key){Point(Center(key));Mouse(true);Mouse(false);Frame();}
    private static void Start(string key){var p=Center(key);Point(p);Mouse(true);Point(p+new Vector2(9,0));}
    private static void Drop(string key,bool after=true)
    {var b=Editor.ItemBounds[key];Point(new(after?b.Max.X-3:b.Min.X+3,(b.Min.Y+b.Max.Y)/2));Mouse(false);Frame();}
    private static void SetText(string key,string text)
    {
        Click(key);var io=ImGui.GetIO();io.AddKeyEvent(ImGuiKey.ModCtrl,true);io.AddKeyEvent(ImGuiKey.A,true);Frame();
        io.AddKeyEvent(ImGuiKey.A,false);io.AddKeyEvent(ImGuiKey.ModCtrl,false);Frame();foreach(var c in text)io.AddInputCharacter(c);Frame();
    }
    public static void RenderPreview()
    {
        Reset();var library=new HuntMacroLibrary(Settings);var custom=library.Add();library.Save(custom.Id,"集合通知","/sh 【<服务器>】请到<flag>集合。");library.Pin(custom.Id);
        RosterPreviewRenderer.RenderWidget("obj/previews/point-macros/a-preset.png",760,730,Frame);
        Editor.Open("S");RosterPreviewRenderer.RenderWidget("obj/previews/point-macros/s-preset.png",760,730,Frame);
        _width=320;RosterPreviewRenderer.RenderWidget("obj/previews/point-macros/narrow.png",380,730,Frame);
        ImGui.GetIO().DisplaySize = new(900,760);
    }
}
