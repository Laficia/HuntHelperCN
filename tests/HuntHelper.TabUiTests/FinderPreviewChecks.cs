using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Managers.Automation;
using System.Numerics;

internal static unsafe class FinderPreviewChecks
{
    private static readonly HuntFinderPreviewUI UI = new();
    private static DRPatrolPreview? _preview;
    private static float _scale = 1;
    private static DRPatrolPreview Example => new()
    {
        World = 101, Territory = 1190, Map = 789, Instance = 2, WorldName = "银泪湖", MapName = "夏劳尼荒野",
        MapScale = 100, State = PatrolState.Moving, Status = "正在飞行巡查，沿途扫描 A 怪", PathStatus = "vnav 当前路径",
        Player = Vector3.Zero, Target = new(550, 0, 250), FoundA = 1,
        Points = [new(new(-500, 0, -200), true), new(new(50, 0, 50), true), new(new(550, 0, 250), false)],
        Path = [new(-10000, 0, 0), Vector3.Zero, new(10000, 0, 0)], Monsters = [new(150, 0, -150)],
    };

    public static void Run()
    {
        var io = ImGui.GetIO();
        io.DisplaySize = new(900, 760); io.AddMousePosEvent(-100, -100); io.AddMouseButtonEvent(0, false);
        var windowsBefore = ImGui.GetCurrentContext().Windows.Size;
        _preview = null; Frame(); Frame();
        Check(ImGui.GetCurrentContext().Windows.Size == windowsBefore && !UI.Visible &&
            ImGui.GetDrawData().CmdListsCount == 0 && UI.CanvasMinimum == UI.CanvasMaximum,
            "An idle preview created drawable content");
        Console.WriteLine("PASS Native DR preview stays silent without an active scan snapshot");

        _preview = Example; Frame(); Frame();
        Check(UI.Visible && ImGui.GetDrawData().CmdListsCount > 0 && UI.WindowSize.X >= 280 && UI.WindowSize.Y >= 320,
            "Active scan preview did not appear");
        Check(Math.Abs((UI.CanvasMaximum - UI.CanvasMinimum).X - (UI.CanvasMaximum - UI.CanvasMinimum).Y) < .01f,
            "Map image canvas was stretched");
        Check(HasCanvasClip(), "Map primitives were not assigned the canvas clip rectangle");
        Console.WriteLine("PASS Native DR preview opens an independent square clipped map canvas");

        var original = UI.WindowSize;
        DragCorner(new(-70, -70)); Frame();
        Check(UI.WindowSize.X < original.X - 40 && UI.WindowSize.Y < original.Y - 40, "Preview resize corner did not shrink the window");
        var smaller = UI.WindowSize;
        DragCorner(new(50, 50)); Frame();
        Check(UI.WindowSize.X > smaller.X + 30 && UI.WindowSize.Y > smaller.Y + 30, "Preview resize corner did not enlarge the window");
        Console.WriteLine("PASS Native DR preview resize corner shrinks and enlarges the floating window");

        var position = UI.WindowPosition;
        var title = position + new Vector2(120, ImGui.GetTextLineHeight() / 2 + 3);
        io.AddMousePosEvent(title.X, title.Y); Frame(); io.AddMouseButtonEvent(0, true); Frame();
        io.AddMousePosEvent(title.X + 30, title.Y + 20); Frame(); io.AddMouseButtonEvent(0, false); Frame();
        Check(Vector2.Distance(position, UI.WindowPosition) > 20, "Preview title bar did not move the window");
        ImGui.SetWindowCollapsed(HuntFinderPreviewUI.WindowTitle, true); Frame();
        Check(UI.Visible && UI.CanvasMinimum == UI.CanvasMaximum, "Collapsed preview continued drawing the map");
        ImGui.SetWindowCollapsed(HuntFinderPreviewUI.WindowTitle, false); Frame(); Frame();
        Check(UI.CanvasMaximum.X > UI.CanvasMinimum.X, "Preview could not expand again");
        Console.WriteLine("PASS Native DR preview can move collapse and expand without changing scan state");

        Check(UI.RenderedPathSegments.Count == 2 && UI.RenderedPathSegments.All(segment =>
            Inside(segment.Start) && Inside(segment.End)), "Real path segments escaped their image bounds");
        _preview = Example with { Path = Array.Empty<Vector3>() }; Frame();
        Check(UI.RenderedPathSegments.Count == 0, "Preview invented a direct route to the navigation target");
        Console.WriteLine("PASS Native DR preview clips real path segments and never invents a line when no path is available");

        _preview = Example with
        {
            Points = [new(new(-500, 0, -200), false), new(new(50, 0, 50), true), new(new(550, 0, 250), false, Passed: true)],
        };
        Frame();
        Check(UI.PatrolledPointCount == 2 && UI.RenderedPoints.Count == 3 &&
            UI.RenderedPoints[1].Color == UI.RenderedPoints[2].Color && UI.RenderedPoints[0].Color != UI.RenderedPoints[2].Color,
            "Passed points did not immediately share the covered color while untouched points stayed pending");
        var pendingColor = UI.RenderedPoints[0].Color;
        _preview = _preview with { Points = _preview.Points.Select(point => point with { Passed = true }).ToArray() };
        Frame();
        Check(UI.PatrolledPointCount == 3 && UI.RenderedPoints.All(point => point.Color != pendingColor) &&
            !_preview.Points[0].Covered, "Visual passage incorrectly required a completed scan or kept the old point color");
        Console.WriteLine("PASS Native DR preview colors passed points immediately and counts visual patrol without claiming coverage");

        Check((UI.DetectionCircleColor >> 24) >= 210 && UI.DetectionCircleThickness == 2 * _scale &&
            (UI.DetectionCircleFillColor >> 24) is > 0 and < 20 && HasVertexColor(UI.DetectionCircleColor),
            "Detection circle did not render a clear cyan stroke with a subtle transparent fill");
        Console.WriteLine("PASS Native DR detection circle uses a visible cyan two-pixel stroke and a subtle fill");

        var savedMinimum = ImGui.GetStyle().WindowMinSize;
        ImGui.GetStyle().WindowMinSize = new(420, 400); io.DisplaySize = new(500, 360); _scale = 1.5f; Frame(); Frame();
        Check(UI.WindowSize.X <= 500 && UI.WindowSize.Y <= 360 && UI.WindowPosition.X >= 0 && UI.WindowPosition.Y >= 0,
            "Scaled preview exceeded a small viewport");
        Check(ImGui.GetStyle().WindowMinSize == new Vector2(420, 400), "Preview leaked its local window style");
        ImGui.GetStyle().WindowMinSize = savedMinimum;
        Console.WriteLine("PASS Native DR preview size constraints adapt to a small scaled viewport without leaking styles");

        _preview = null; Frame(); Frame();
        Check(!UI.Visible && ImGui.GetDrawData().CmdListsCount == 0 && UI.RenderedPathSegments.Count == 0,
            "Stopped scan left a floating preview or stale path visible");
        Console.WriteLine("PASS Native DR preview disappears and clears rendered path state when the scan stops");

        Projection();
        io.DisplaySize = new(900, 760); _scale = 1;
    }

    private static void Projection()
    {
        var min = new Vector2(10, 20); const float size = 320, scale = 100;
        Check(HuntFinderPreviewProjection.TryProject(new(100, 999, 200), scale, min, size, out var actual), "Valid map point was rejected");
        var expected = min + new Vector2((2048f / scale + 100f / 50) / 41, (2048f / scale + 200f / 50) / 41) * size;
        Check(Vector2.Distance(actual, expected) < .001f, "Preview projection differs from the existing HHT image coordinates");
        Check(HuntFinderPreviewProjection.TryProject(new(100, -999, 200), scale, min, size, out var otherHeight) && actual == otherHeight,
            "2D map projection incorrectly used the world's elevation");
        Check(!HuntFinderPreviewProjection.TryProject(Vector3.Zero, 0, min, size, out _) &&
            !HuntFinderPreviewProjection.TryProject(new(float.NaN, 0, 0), scale, min, size, out _), "Invalid map input produced usable drawing coordinates");
        Check(HuntFinderPreviewProjection.TryClipLine(new(-100, 50), new(200, 50), Vector2.Zero, new(100), out var start, out var end) &&
            start == new Vector2(0, 50) && end == new Vector2(100, 50), "Line intersection was not clipped to the image");
        Check(!HuntFinderPreviewProjection.TryClipLine(new(-100, -50), new(200, -50), Vector2.Zero, new(100), out _, out _),
            "An entirely outside line was retained");
        Console.WriteLine("PASS Pure DR map projection matches HHT world coordinates and clips invalid or off-map geometry");
    }

    private static bool Inside(Vector2 point) => HuntFinderPreviewProjection.Contains(point, UI.CanvasMinimum, UI.CanvasMaximum);
    public static void RenderPreview()
    {
        _scale = 1;
        _preview = Example with
        {
            Status = "模拟数据 · 正在飞行巡查", PathStatus = "当前导航路径 · 沿途扫描",
            Path = [new(-620, 0, 420), new(-350, 0, 350), new(-180, 0, 100), Vector3.Zero, new(240, 0, 100), new(550, 0, 250)],
            Points = [new(new(-620, 0, 420), false, Passed: true), new(new(-350, 0, -240), true), new(new(-180, 0, 100), false, Passed: true),
                new(new(0, 0, -530), false), new(new(240, 0, 100), false, Passed: true), new(new(550, 0, 250), false),
                new(new(620, 0, -400), false), new(new(-590, 0, -520), false), new(new(470, 0, 690), false)],
        };
        RosterPreviewRenderer.RenderWidget("obj/previews/dr-finder/preview-grid.png", 430, 510, () =>
        {
            Frame();
            ImGui.SetWindowSize(HuntFinderPreviewUI.WindowTitle, new(390, 470));
            ImGui.SetWindowPos(HuntFinderPreviewUI.WindowTitle, new(18, 18));
        });
        _preview = null;
    }
    private static bool HasCanvasClip()
    {
        var data = ImGui.GetDrawData();
        for (var index = 0; index < data.CmdListsCount; index++)
        {
            var list = new ImDrawListPtr(data.CmdLists[index]);
            for (var command = 0; command < list.CmdBuffer.Size; command++)
            {
                var clip = list.CmdBuffer.Data[command].ClipRect;
                if (Vector2.Distance(new(clip.X, clip.Y), UI.CanvasMinimum) < .1f &&
                    Vector2.Distance(new(clip.Z, clip.W), UI.CanvasMaximum) < .1f) return true;
            }
        }
        return false;
    }
    private static bool HasVertexColor(uint color)
    {
        var data = ImGui.GetDrawData();
        for (var index = 0; index < data.CmdListsCount; index++)
        {
            var list = new ImDrawListPtr(data.CmdLists[index]);
            for (var vertex = 0; vertex < list.VtxBuffer.Size; vertex++)
                if (list.VtxBuffer.Data[vertex].Col == color) return true;
        }
        return false;
    }
    private static void Frame() { ImGui.NewFrame(); UI.Draw(_preview, null, _scale); ImGui.Render(); }
    private static void DragCorner(Vector2 delta)
    {
        var io = ImGui.GetIO(); var corner = UI.WindowPosition + UI.WindowSize - new Vector2(4);
        io.AddMousePosEvent(corner.X, corner.Y); Frame(); io.AddMouseButtonEvent(0, true); Frame();
        io.AddMousePosEvent(corner.X + delta.X, corner.Y + delta.Y); Frame(); io.AddMouseButtonEvent(0, false); Frame();
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
}
