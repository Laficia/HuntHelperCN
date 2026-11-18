using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;

namespace HuntHelper.Gui;

internal static class CompactTrainTable
{
    private const float MinimumActionWidth = 50;
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
        var minimumActions = 3 * MinimumActionWidth * scale + 2 * style.ItemSpacing.X;
        var fixedWidth = (handles * CompactTrainColumnResize.HandleWidth + DeathWidth) * scale + minimumActions + 2 * columns * style.CellPadding.X;
        var contentHeight = ImGui.GetTextLineHeight() + 2 * style.CellPadding.Y + rowCount * RowHeight(scale);
        var availableWidth = ImGui.GetContentRegionAvail().X - (contentHeight > height ? style.ScrollbarSize : 0);
        if (locationWidth is { } location)
        {
            // Preserve each saved width as the window grows. In tighter windows,
            // share the space above the minimum so the row controls stay visible.
            const float minimumText = CompactTrainColumnResize.MinimumWidth;
            var preferredExtra = monsterWidth + location - 2 * minimumText;
            if (preferredExtra > 0)
            {
                var availableExtra = (availableWidth - fixedWidth) / scale - 2 * minimumText;
                var fraction = Math.Clamp(availableExtra / preferredExtra, 0, 1);
                monsterWidth = minimumText + (monsterWidth - minimumText) * fraction;
                locationWidth = minimumText + (location - minimumText) * fraction;
            }
        }
        var minimumWidth = fixedWidth + (monsterWidth + (locationWidth ?? 0)) * scale;
        var needsHorizontalScroll = availableWidth + .5f < minimumWidth;
        if (needsHorizontalScroll) flags |= ImGuiTableFlags.ScrollX;

        // An explicit inner width bounds stretch sizing. Use the viewport's own
        // width whenever it can hold the table without a horizontal scrollbar.
        if (!ImGui.BeginTable("CompactTrainRows", columns, flags, new(0, height), needsHorizontalScroll ? minimumWidth : 0)) return false;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("怪物", ImGuiTableColumnFlags.WidthFixed, monsterWidth * scale);
        ImGui.TableSetupColumn("##怪物列宽", ImGuiTableColumnFlags.WidthFixed, CompactTrainColumnResize.HandleWidth * scale);
        if (locationWidth.HasValue)
        {
            ImGui.TableSetupColumn("地点", ImGuiTableColumnFlags.WidthFixed, locationWidth.Value * scale);
            ImGui.TableSetupColumn("##地点列宽", ImGuiTableColumnFlags.WidthFixed, CompactTrainColumnResize.HandleWidth * scale);
        }
        ImGui.TableSetupColumn("操作", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("死亡", ImGuiTableColumnFlags.WidthFixed, DeathWidth * scale);
        return true;
    }

    public static Vector2 ActionButtonSize(float scale)
    {
        var width = (ImGui.GetContentRegionAvail().X - 2 * ImGui.GetStyle().ItemSpacing.X) / 3;
        return new(Math.Max(MinimumActionWidth * scale, MathF.Floor(width)), ActionHeight * scale);
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
