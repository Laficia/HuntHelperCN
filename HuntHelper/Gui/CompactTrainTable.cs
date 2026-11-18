using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HuntHelper.Gui;

internal static class CompactTrainTable
{
    private const float PreferredActionWidth = 50;
    private const int ActionCount = 3;
    private const float DeathWidth = 34;
    private const float ActionHeight = 28;

    public static float RowHeight(float scale) => ActionHeight * scale + 2 * ImGui.GetStyle().CellPadding.Y;

    public static bool Begin(float height, float monsterWidth, float? locationWidth, float scale, int rowCount)
    {
        var flags = ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.ScrollY |
            ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoSavedSettings;
        var style = ImGui.GetStyle();
        var columns = locationWidth.HasValue ? 6 : 4;
        var handles = locationWidth.HasValue ? 2 : 1;
        var minimumAction = MinimumActionWidth();
        var deathWidth = MathF.Ceiling(Math.Max(DeathWidth * scale, Math.Max(ImGui.CalcTextSize("死亡").X, ImGui.GetFrameHeight())));
        var fixedWidth = handles * CompactTrainColumnResize.HandleWidth * scale + deathWidth +
            (ActionCount - 1) * style.ItemSpacing.X + 2 * columns * style.CellPadding.X;
        var contentHeight = ImGui.GetTextLineHeight() + 2 * style.CellPadding.Y + rowCount * RowHeight(scale);
        var availableWidth = ImGui.GetContentRegionAvail().X - (contentHeight > height ? style.ScrollbarSize : 0);
        var preferredMonster = monsterWidth * scale;
        var preferredLocation = (locationWidth ?? 0) * scale;
        var minimumMonster = Math.Min(preferredMonster, MathF.Ceiling(ImGui.CalcTextSize("怪物").X));
        var minimumLocation = Math.Min(preferredLocation, MathF.Ceiling(ImGui.CalcTextSize("地点").X));
        var minimumWidth = MathF.Ceiling(fixedWidth + minimumMonster + minimumLocation + ActionCount * minimumAction);
        var preferredWidth = fixedWidth + preferredMonster + preferredLocation +
            ActionCount * Math.Max(minimumAction, PreferredActionWidth * scale);
        // Compress both text columns and the buttons within the available width.
        // Leave the saved drag widths intact so they return when the window grows.
        var fraction = Math.Clamp((availableWidth - minimumWidth) / Math.Max(1, preferredWidth - minimumWidth), 0, 1);
        var fittedMonster = MathF.Floor(minimumMonster + (preferredMonster - minimumMonster) * fraction);
        var fittedLocation = MathF.Floor(minimumLocation + (preferredLocation - minimumLocation) * fraction);
        var needsHorizontalScroll = availableWidth < minimumWidth;
        if (needsHorizontalScroll) flags |= ImGuiTableFlags.ScrollX;

        // An explicit inner width bounds stretch sizing. Use the viewport's own
        // width whenever it can hold the table without a horizontal scrollbar.
        if (!ImGui.BeginTable("CompactTrainRows", columns, flags, new(0, height), needsHorizontalScroll ? minimumWidth : 0)) return false;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("怪物", ImGuiTableColumnFlags.WidthFixed, fittedMonster);
        ImGui.TableSetupColumn("##怪物列宽", ImGuiTableColumnFlags.WidthFixed, CompactTrainColumnResize.HandleWidth * scale);
        if (locationWidth.HasValue)
        {
            ImGui.TableSetupColumn("地点", ImGuiTableColumnFlags.WidthFixed, fittedLocation);
            ImGui.TableSetupColumn("##地点列宽", ImGuiTableColumnFlags.WidthFixed, CompactTrainColumnResize.HandleWidth * scale);
        }
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("死亡", ImGuiTableColumnFlags.WidthFixed, deathWidth);
        return true;
    }

    public static Vector2 ActionButtonSize(float scale)
    {
        var width = (ImGui.GetContentRegionAvail().X - (ActionCount - 1) * ImGui.GetStyle().ItemSpacing.X) / ActionCount;
        return new(Math.Max(MinimumActionWidth(), MathF.Floor(width)), ActionHeight * scale);
    }

    private static float MinimumActionWidth()
    {
        var text = Math.Max(ImGui.CalcTextSize("传送").X, Math.Max(ImGui.CalcTextSize("移动").X, ImGui.CalcTextSize("坐标").X));
        return MathF.Ceiling(text + 2 * ImGui.GetStyle().FramePadding.X);
    }

    public static bool LocationToggleRequested(bool open, bool locationVisible)
    {
        const string popup = "CompactTrainColumns";
        if (open) ImGui.OpenPopup(popup);
        if (!ImGui.BeginPopup(popup)) return false;
        var toggle = ImGui.MenuItem(locationVisible ? "隐藏地点列" : "显示地点列");
        ImGui.EndPopup();
        return toggle;
    }
}
