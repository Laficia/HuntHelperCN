using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Managers.Hunts.Models;

internal static unsafe class Program
{
    private static readonly WorldTabBar Tabs = new();
    private static readonly List<uint> Order = [101, 102, 103];
    private static HuntTrainWorld[] Worlds =
    [new(101, "Alpha", "", 1), new(102, "Beta", "", 2), new(103, "Gamma", "", 3)];
    private static WorldTabAction _action;

    private static int Main(string[] args)
    {
        ImGui.CreateContext();
        try
        {
            var io = ImGui.GetIO();
            io.IniFilename = null;
            io.DisplaySize = new Vector2(900, 400);
            io.DeltaTime = 1f / 60;
            io.Fonts.AddFontDefault();
            io.Fonts.Build();
            Frame(); Frame();
            Drag(101, 103, after: true);
            Expect(102, 103, 101);
            Frame();
            ExpectVisible(102, 103, 101);
            Console.WriteLine("PASS Native ImGui left drag changes both saved and rendered tab order");
            Drag(101, 102, after: false);
            Expect(101, 102, 103);
            Frame();
            ExpectVisible(101, 102, 103);
            Console.WriteLine("PASS Native ImGui reverse drag returns the tab to the first position");
            if (!ImGui.GetDragDropPayload().IsNull) throw new Exception("Unexpected native drag/drop payload");
            Console.WriteLine("PASS No native drag/drop payload or default yellow target is created");
            MenuClick(102, 0);
            if (_action.Selected != 102) throw new Exception("Right-click Select did not select Beta");
            Console.WriteLine("PASS Native right-click menu selects the requested server");
            MenuClick(102, 1); Expect(102, 101, 103); Frame(); ExpectVisible(102, 101, 103);
            MenuClick(102, 2); Expect(101, 102, 103); Frame(); ExpectVisible(101, 102, 103);
            Console.WriteLine("PASS Native right-click Left and Right move the rendered tabs");
            MenuClick(102, -1);
            if (_action.Deleted != 102) throw new Exception("Right-click Delete did not target Beta");
            Console.WriteLine("PASS Native right-click Delete targets only its server");
            Worlds = Worlds.Where(w => w.WorldID != 102).ToArray(); Frame(); ExpectVisible(101, 103);
            Worlds = []; Frame(); ExpectVisible(); Expect();
            Console.WriteLine("PASS Removed and empty servers leave no stale rendered tabs");
            WindowResizeChecks.Run();
            VersionTabUiChecks.Run();
            FinderActionChecks.Run();
            RosterPresentationChecks.Run();
            if (args.Contains("--render-roster")) RosterPreviewRenderer.Render(args.Contains("--show-missing"));
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { ImGui.DestroyContext(); }
    }

    private static void Frame()
    {
        ImGui.NewFrame();
        ImGui.SetNextWindowPos(Vector2.Zero);
        ImGui.SetNextWindowSize(new Vector2(880, 300));
        ImGui.Begin("TestHost", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
        _action = Tabs.Draw(Worlds, 101, Order);
        ImGui.End();
        ImGui.Render();
    }

    private static Vector2 Center(uint id)
    {
        var b = Tabs.VisibleTabs.Single(b => b.WorldId == id);
        return (b.Min + b.Max) / 2;
    }

    private static void Drag(uint source, uint target, bool after)
    {
        var io = ImGui.GetIO();
        var start = Center(source);
        io.AddMousePosEvent(start.X, start.Y); Frame();
        io.AddMouseButtonEvent(0, true); Frame();
        io.AddMousePosEvent(start.X + 12, start.Y); Frame();
        var dest = Tabs.VisibleTabs.Single(b => b.WorldId == target);
        io.AddMousePosEvent(after ? dest.Max.X - 3 : dest.Min.X + 3, (dest.Min.Y + dest.Max.Y) / 2); Frame();
        if (!ImGui.GetDragDropPayload().IsNull) throw new Exception("Default drop target became active");
        io.AddMouseButtonEvent(0, false); Frame();
        if (!_action.OrderChanged) throw new Exception("Drop did not change the tab order");
    }

    private static void Expect(params uint[] expected)
    {
        if (!expected.SequenceEqual(Order)) throw new Exception($"Wrong order: {string.Join(',', Order)}");
    }

    private static void MenuClick(uint world, int row)
    {
        var io = ImGui.GetIO();
        var tab = Center(world);
        io.AddMousePosEvent(tab.X, tab.Y); Frame();
        io.AddMouseButtonEvent(1, true); Frame();
        io.AddMouseButtonEvent(1, false); Frame(); Frame();
        var stack = ImGui.GetCurrentContext().OpenPopupStack;
        if (stack.Size != 1 || stack[0].Window == null) throw new Exception("Server context menu did not open");
        var popup = stack[0].Window;
        var style = ImGui.GetStyle();
        var x = popup->Pos.X + style.WindowPadding.X + 24;
        var y = row < 0
            ? popup->Pos.Y + popup->Size.Y - style.WindowPadding.Y - ImGui.GetTextLineHeight() / 2
            : popup->Pos.Y + style.WindowPadding.Y + row * ImGui.GetTextLineHeightWithSpacing() + ImGui.GetTextLineHeight() / 2;
        io.AddMousePosEvent(x, y); Frame();
        io.AddMouseButtonEvent(0, true); Frame();
        io.AddMouseButtonEvent(0, false); Frame();
    }
    private static void ExpectVisible(params uint[] expected)
    {
        var visible = Tabs.VisibleTabs.OrderBy(b => b.Min.X).Select(b => b.WorldId);
        if (!expected.SequenceEqual(visible)) throw new Exception("Rendered order differs from saved order");
    }
}
