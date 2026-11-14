using Dalamud.Bindings.ImGui;
using HuntHelper.Managers.Automation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

/// <summary>Uses the same world-to-image coordinates as the existing HHT map window.</summary>
public static class HuntFinderPreviewProjection
{
    public static bool TryProject(Vector3 world, float mapScale, Vector2 minimum, float size, out Vector2 screen)
    {
        screen = default;
        var maximumCoordinate = -.42f * mapScale + 83;
        if (!float.IsFinite(mapScale) || mapScale <= 0 || maximumCoordinate <= 0 ||
            !float.IsFinite(size) || size <= 0 || !Finite(world.X) || !Finite(world.Y) || !Finite(world.Z) ||
            !Finite(minimum.X) || !Finite(minimum.Y)) return false;
        var coordinates = new Vector2(2048 / mapScale + world.X / 50, 2048 / mapScale + world.Z / 50);
        screen = minimum + coordinates / maximumCoordinate * size;
        return Finite(screen.X) && Finite(screen.Y);
    }

    public static bool TryClipLine(Vector2 first, Vector2 second, Vector2 minimum, Vector2 maximum,
        out Vector2 start, out Vector2 end)
    {
        start = end = default;
        if (!Finite(first.X) || !Finite(first.Y) || !Finite(second.X) || !Finite(second.Y) ||
            !Finite(minimum.X) || !Finite(minimum.Y) || !Finite(maximum.X) || !Finite(maximum.Y) ||
            maximum.X < minimum.X || maximum.Y < minimum.Y) return false;
        var delta = second - first;
        if (!Finite(delta.X) || !Finite(delta.Y)) return false;
        var entry = 0f; var exit = 1f;
        if (!Clip(-delta.X, first.X - minimum.X, ref entry, ref exit) ||
            !Clip(delta.X, maximum.X - first.X, ref entry, ref exit) ||
            !Clip(-delta.Y, first.Y - minimum.Y, ref entry, ref exit) ||
            !Clip(delta.Y, maximum.Y - first.Y, ref entry, ref exit)) return false;
        start = Vector2.Clamp(first + entry * delta, minimum, maximum);
        end = Vector2.Clamp(first + exit * delta, minimum, maximum);
        return true;
    }

    public static bool Contains(Vector2 point, Vector2 minimum, Vector2 maximum) =>
        point.X >= minimum.X && point.X <= maximum.X && point.Y >= minimum.Y && point.Y <= maximum.Y;

    private static bool Finite(float value) => float.IsFinite(value);
    private static bool Clip(float direction, float offset, ref float entry, ref float exit)
    {
        if (direction == 0) return offset >= 0;
        var progress = offset / direction;
        if (direction < 0) { if (progress > exit) return false; entry = Math.Max(entry, progress); }
        else { if (progress < entry) return false; exit = Math.Min(exit, progress); }
        return true;
    }
}

/// <summary>Draws a detached scan snapshot; it never queries navigation or owns textures.</summary>
public sealed class HuntFinderPreviewUI
{
    public const string WindowTitle = "DR 找怪 · 路线预览###HHTDRPreview";
    private static readonly Vector4 Cyan = new(.38f, .80f, .96f, 1);
    private static readonly Vector4 Pending = new(.95f, .43f, .45f, 1);
    private static readonly Vector4 Covered = new(.32f, .79f, .67f, 1);
    private static readonly Vector4 Gold = new(1, .78f, .34f, 1);
    private readonly List<(Vector2 Start, Vector2 End)> _segments = new();
    private readonly List<(Vector2 Position, uint Color)> _points = new();

    internal bool Visible { get; private set; }
    internal Vector2 WindowPosition { get; private set; }
    internal Vector2 WindowSize { get; private set; }
    internal Vector2 CanvasMinimum { get; private set; }
    internal Vector2 CanvasMaximum { get; private set; }
    internal IReadOnlyList<(Vector2 Start, Vector2 End)> RenderedPathSegments => _segments;
    internal IReadOnlyList<(Vector2 Position, uint Color)> RenderedPoints => _points;
    internal int PatrolledPointCount { get; private set; }
    internal uint DetectionCircleColor { get; private set; }
    internal uint DetectionCircleFillColor { get; private set; }
    internal float DetectionCircleThickness { get; private set; }

    public void Draw(DRPatrolPreview? preview, ImTextureID? texture, float uiScale = 1)
    {
        Visible = false;
        CanvasMinimum = CanvasMaximum = default;
        _segments.Clear();
        _points.Clear();
        PatrolledPointCount = 0;
        DetectionCircleColor = DetectionCircleFillColor = 0;
        DetectionCircleThickness = 0;
        if (preview == null) return;
        var scale = float.IsFinite(uiScale) && uiScale > 0 ? uiScale : 1;
        var viewport = ImGui.GetMainViewport();
        var maximum = Vector2.Max(new Vector2(1), viewport.WorkSize - new Vector2(12 * scale));
        var minimum = Vector2.Min(new Vector2(280, 320) * scale, maximum);
        ImGui.SetNextWindowSize(Vector2.Clamp(new Vector2(390, 470) * scale, minimum, maximum), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(minimum, maximum);
        ImGui.SetNextWindowPos(viewport.WorkPos + new Vector2(18, 24) * scale, ImGuiCond.FirstUseEver);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, minimum);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 7 * scale);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(.055f, .075f, .105f, .97f));
        ImGui.PushStyleColor(ImGuiCol.TitleBg, new Vector4(.075f, .105f, .145f, 1));
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, new Vector4(.10f, .16f, .21f, 1));
        ImGui.PushStyleColor(ImGuiCol.ResizeGrip, new Vector4(.38f, .70f, .85f, .5f));
        ImGui.PushStyleColor(ImGuiCol.ResizeGripHovered, Cyan);
        ImGui.PushStyleColor(ImGuiCol.ResizeGripActive, Cyan);
        try
        {
            var expanded = ImGui.Begin(WindowTitle, ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoFocusOnAppearing);
            try
            {
                Visible = true;
                WindowSize = ImGui.GetWindowSize();
                var latest = viewport.WorkPos + Vector2.Max(Vector2.Zero, viewport.WorkSize - WindowSize);
                var position = Vector2.Clamp(ImGui.GetWindowPos(), viewport.WorkPos, latest);
                if (position != ImGui.GetWindowPos()) ImGui.SetWindowPos(position);
                WindowPosition = position;
                if (!expanded) return;
                DrawContents(preview, texture, scale);
            }
            finally { ImGui.End(); }
        }
        finally { ImGui.PopStyleColor(6); ImGui.PopStyleVar(2); }
    }

    private void DrawContents(DRPatrolPreview preview, ImTextureID? texture, float scale)
    {
        var location = $"{preview.MapName} · {preview.WorldName}" + (preview.Instance > 0 ? $" · {preview.Instance}线" : "");
        ImGui.TextColored(new Vector4(.89f, .94f, .98f, 1), Fit(location, ImGui.GetContentRegionAvail().X));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(location);
        var completed = preview.Points.Count(point => point.Covered || point.Passed);
        PatrolledPointCount = completed;
        ImGui.TextColored(new Vector4(.62f, .73f, .80f, 1), $"已巡查 {completed}/{preview.Points.Count}  ·  A怪 {preview.FoundA}");
        var progressStart = ImGui.GetCursorScreenPos();
        var progressWidth = Math.Max(1, ImGui.GetContentRegionAvail().X);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(progressStart, progressStart + new Vector2(progressWidth, 3 * scale), Color(new(.15f, .22f, .27f, 1)), 2 * scale);
        if (preview.Points.Count > 0 && completed > 0)
            draw.AddRectFilled(progressStart, progressStart + new Vector2(progressWidth * completed / preview.Points.Count, 3 * scale), Color(Covered), 2 * scale);
        ImGui.Dummy(new Vector2(0, 7 * scale));

        var available = ImGui.GetContentRegionAvail();
        var legends = LegendItems(available.X, scale);
        var legendRows = LegendRows(legends, available.X, scale);
        var footer = (legendRows + 1) * ImGui.GetTextLineHeightWithSpacing() + 7 * scale;
        var side = Math.Max(1, Math.Min(available.X, available.Y - footer));
        var minimum = ImGui.GetCursorScreenPos() + new Vector2(Math.Max(0, (available.X - side) / 2), 0);
        var maximum = minimum + new Vector2(side);
        CanvasMinimum = minimum; CanvasMaximum = maximum;
        ImGui.SetCursorScreenPos(minimum);
        ImGui.InvisibleButton("##DRPreviewCanvas", new Vector2(side));
        var hovered = ImGui.IsItemHovered();
        DrawCanvas(preview, texture, scale, draw, minimum, maximum, side);
        if (hovered) ImGui.SetTooltip("红色：尚未巡查的刷新点\n青绿：扫描圈内、实际经过范围或已完成覆盖的点\n经过状态仅为视觉巡查，不改变怪物记录，也不代替正式扫描确认\n青蓝：当前真实导航路径；圆圈为固定400米检测假设\n金色菱形：本图本线已发现的 A 怪");
        ImGui.Spacing();
        var used = 0f;
        foreach (var legend in legends)
        {
            var width = LegendWidth(legend.Text, scale);
            if (used > 0 && used + 8 * scale + width <= available.X) { ImGui.SameLine(0, 8 * scale); used += 8 * scale; }
            else used = 0;
            Legend(legend.Color, legend.Text, scale);
            used += width;
        }
        var status = StateLabel(preview.State);
        if (preview.PathStatus.Contains("终点距")) status += " · 路线未达目标";
        if (string.IsNullOrWhiteSpace(status)) status = StateLabel(preview.State);
        ImGui.TextColored(new Vector4(.60f, .69f, .76f, 1), Fit(status, ImGui.GetContentRegionAvail().X));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{StateLabel(preview.State)}\n{preview.Status}\n{preview.PathStatus}");
    }

    private void DrawCanvas(DRPatrolPreview preview, ImTextureID? texture, float scale, ImDrawListPtr draw,
        Vector2 minimum, Vector2 maximum, float side)
    {
        draw.AddRectFilled(minimum, maximum, Color(new(.075f, .10f, .13f, 1)));
        draw.PushClipRect(minimum, maximum, true);
        try
        {
            if (texture is { } image)
                draw.AddImage(image, minimum, maximum, Vector2.Zero, Vector2.One, Color(new(.94f, .97f, 1, .94f)));
            else
            {
                var grid = Color(new(.24f, .35f, .40f, .32f));
                for (var index = 1; index < 8; index++)
                {
                    var offset = side * index / 8;
                    draw.AddLine(minimum + new Vector2(offset, 0), minimum + new Vector2(offset, side), grid);
                    draw.AddLine(minimum + new Vector2(0, offset), minimum + new Vector2(side, offset), grid);
                }
                draw.AddText(minimum + new Vector2(9 * scale), Color(new(.60f, .70f, .76f, 1)), "底图加载中");
            }
            bool Project(Vector3 point, out Vector2 projected) => HuntFinderPreviewProjection.TryProject(point, preview.MapScale, minimum, side, out projected);
            if (Project(preview.Player, out var player))
            {
                if (Project(preview.Player + new Vector3(400, 0, 0), out var edge))
                {
                    var radius = Vector2.Distance(player, edge);
                    DetectionCircleColor = Color(new(.38f, .80f, .96f, .85f));
                    DetectionCircleFillColor = Color(new(.38f, .80f, .96f, .045f));
                    DetectionCircleThickness = 2 * scale;
                    draw.AddCircleFilled(player, radius, DetectionCircleFillColor, 72);
                    draw.AddCircle(player, radius, DetectionCircleColor, 72, DetectionCircleThickness);
                }
            }
            for (var index = 1; index < preview.Path.Count; index++)
            {
                if (!Project(preview.Path[index - 1], out var first) || !Project(preview.Path[index], out var second) ||
                    !HuntFinderPreviewProjection.TryClipLine(first, second, minimum, maximum, out var start, out var end)) continue;
                _segments.Add((start, end));
                draw.AddLine(start, end, Color(Cyan), 2 * scale);
            }
            foreach (var point in preview.Points)
            {
                if (!Project(point.Position, out var position) || !HuntFinderPreviewProjection.Contains(position, minimum, maximum)) continue;
                var patrolled = point.Covered || point.Passed;
                var color = Color(patrolled ? Covered : Pending);
                _points.Add((position, color));
                draw.AddCircleFilled(position, (patrolled ? 2.7f : 3.4f) * scale, color, 12);
            }
            foreach (var monster in preview.Monsters)
            {
                if (!Project(monster, out var position) || !HuntFinderPreviewProjection.Contains(position, minimum, maximum)) continue;
                var radius = 6 * scale;
                draw.AddQuadFilled(position + new Vector2(0, -radius), position + new Vector2(radius, 0),
                    position + new Vector2(0, radius), position + new Vector2(-radius, 0), Color(Gold));
            }
            if (preview.Target is { } target && Project(target, out var targetPosition))
            {
                draw.AddCircle(targetPosition, 8 * scale, Color(Cyan), 24, 2 * scale);
                draw.AddCircleFilled(targetPosition, 2 * scale, Color(Cyan), 12);
            }
            if (Project(preview.Player, out player))
            {
                draw.AddCircleFilled(player, 5 * scale, Color(Cyan), 20);
                draw.AddCircle(player, 6 * scale, Color(new(.94f, .98f, 1, 1)), 24, 1.5f * scale);
            }
        }
        finally { draw.PopClipRect(); }
        draw.AddRect(minimum, maximum, Color(new(.27f, .40f, .47f, .8f)), 0, ImDrawFlags.None, 1 * scale);
    }

    private static void Legend(Vector4 color, string text, float scale)
    {
        var position = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddCircleFilled(position + new Vector2(3 * scale, ImGui.GetTextLineHeight() / 2), 2.5f * scale, Color(color));
        ImGui.Dummy(new Vector2(7 * scale, ImGui.GetTextLineHeight()));
        ImGui.SameLine(0, 3 * scale); ImGui.TextDisabled(text);
    }

    private static (Vector4 Color, string Text)[] LegendItems(float width, float scale)
    {
        var full = LegendWidth("待巡查", scale) + LegendWidth("已巡查", scale) +
            LegendWidth("路线", scale) + LegendWidth("A怪", scale) + 24 * scale <= width;
        return [(Pending, full ? "待巡查" : "待查"), (Covered, full ? "已巡查" : "已查"), (Cyan, "路线"), (Gold, "A怪")];
    }

    private static float LegendWidth(string text, float scale) => 10 * scale + ImGui.CalcTextSize(text).X;
    private static int LegendRows((Vector4 Color, string Text)[] legends, float width, float scale)
    {
        var rows = 1; var used = 0f;
        foreach (var legend in legends)
        {
            var item = LegendWidth(legend.Text, scale);
            if (used > 0 && used + 8 * scale + item > width) { rows++; used = 0; }
            else if (used > 0) used += 8 * scale;
            used += item;
        }
        return rows;
    }

    private static uint Color(Vector4 color) => ImGui.ColorConvertFloat4ToU32(color);
    private static string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) return text;
        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + "…").X > width) length--;
        return length == 0 ? "" : text[..length] + "…";
    }

    private static string StateLabel(PatrolState state) => state switch
    {
        PatrolState.PreparingWorld => "准备服务器", PatrolState.ChangingWorld => "正在跨服",
        PatrolState.PreparingMap => "准备地图", PatrolState.Teleporting => "水晶传送中",
        PatrolState.PreparingMove => "准备寻路", PatrolState.Moving => "飞行巡查中",
        PatrolState.Scanning => "停车观察中", PatrolState.Completed => "巡查完成", _ => "已停止",
    };
}
