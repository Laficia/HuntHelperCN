using System.Numerics;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;

internal static class CompactColumnChecks
{
    public static void Run()
    {
        var resize = new CompactTrainColumnResize();
        var resizeLocation = new CompactTrainColumnResize(defaultWidth: 128);
        float savedWidth = 120, actionsX = 0, deathRight = 0, tableRight = 0, scrollX = 0, scrollY = 0, actionWidth = 0;
        float savedLocationWidth = 128;
        int saves = 0, rows = 4;
        float windowWidth = 700;
        Vector2 handle = default;
        Vector2 locationHandle = default, locationHeader = default, monsterHeader = default;
        bool showLocation = false, columnMenuOpen = false;

        void Frame()
        {
            ImGui.NewFrame();
            ImGui.SetNextWindowPos(Vector2.Zero);
            ImGui.SetNextWindowSize(new(windowWidth, 300));
            ImGui.Begin("CompactWidth", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(6, 8));
            ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 3));
            if (CompactTrainTable.Begin(210, resize.Width(savedWidth), showLocation ? resizeLocation.Width(savedLocationWidth) : null, 1, rows))
            {
                int actionsColumn = showLocation ? 4 : 2;
                tableRight = ImGui.GetWindowPos().X + ImGui.GetWindowSize().X;
                scrollX = ImGui.GetScrollMaxX();
                scrollY = ImGui.GetScrollMaxY();
                ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
                ImGui.TableSetColumnIndex(0); ImGui.TableHeader("怪物");
                var openColumns = ImGui.IsItemClicked(ImGuiMouseButton.Right);
                monsterHeader = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                ImGui.TableSetColumnIndex(1);
                if (resize.Draw(1, ref savedWidth)) saves++;
                handle = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                if (showLocation)
                {
                    ImGui.TableSetColumnIndex(2); ImGui.TableHeader("地点");
                    openColumns |= ImGui.IsItemClicked(ImGuiMouseButton.Right);
                    locationHeader = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                    ImGui.TableSetColumnIndex(3);
                    if (resizeLocation.Draw(1, ref savedLocationWidth, "地点")) saves++;
                    locationHandle = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
                }
                ImGui.TableSetColumnIndex(actionsColumn);
                var buttonSize = CompactTrainTable.ActionButtonSize(1);
                actionWidth = buttonSize.X;
                ImGui.TableHeader("操作");
                actionsX = ImGui.GetItemRectMin().X;
                ImGui.TableSetColumnIndex(actionsColumn + 1); ImGui.TableHeader("死亡");
                deathRight = ImGui.GetItemRectMax().X;
                for (int row = 0; row < rows; row++)
                {
                    ImGui.PushID(row);
                    ImGui.TableNextRow(ImGuiTableRowFlags.None, CompactTrainTable.RowHeight(1));
                    ImGui.TableSetColumnIndex(actionsColumn);
                    ImGui.Button("传送", buttonSize); ImGui.SameLine();
                    ImGui.Button("移动", buttonSize); ImGui.SameLine();
                    ImGui.Button("坐标", buttonSize);
                    ImGui.PopID();
                }
                ImGui.EndTable();
                if (CompactTrainTable.LocationToggleRequested(openColumns, showLocation)) showLocation = !showLocation;
                columnMenuOpen = ImGui.IsPopupOpen("CompactTrainColumns");
            }
            ImGui.PopStyleVar(2);
            ImGui.End();
            ImGui.Render();
        }

        void Point(Vector2 point) { ImGui.GetIO().AddMousePosEvent(point.X, point.Y); Frame(); }
        void Mouse(bool down) { ImGui.GetIO().AddMouseButtonEvent(0, down); Frame(); }
        void Hold() { for (int i = 0; i < 30; i++) Frame(); }
        static void Check(bool result, string scenario)
        {
            if (!result) throw new Exception("Compact column resize failed: " + scenario);
        }

        Frame(); Frame();
        Check(scrollX <= 1 && Math.Abs(tableRight - deathRight) <= 5, $"wide list fills the table, death {deathRight}, table {tableRight}, scroll X {scrollX}");
        float wideAction = actionWidth;
        float initialActions = actionsX;
        var start = handle;
        Point(start); Mouse(true); Point(start + new Vector2(39, 0)); Mouse(false); Frame();
        Check(saves == 0 && actionsX == initialActions, "short press");

        Point(handle); Mouse(true); Hold(); Point(start + new Vector2(52, 0)); Frame();
        Check(saves == 0 && actionsX == initialActions + 52, $"live preview, action X {actionsX}, initial X {initialActions}");
        Mouse(false); Frame();
        Check(saves == 1 && Math.Abs(savedWidth - 172) < .01f && actionsX == initialActions + 52, "release commits once");

        start = handle;
        Point(start); Mouse(true); Hold(); Point(start - new Vector2(160, 0)); Frame();
        Check(resize.Width(savedWidth) == 80 && saves == 1, "minimum clamps during drag");
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape, true); Frame();
        ImGui.GetIO().AddKeyEvent(ImGuiKey.Escape, false); Frame();
        Mouse(false); Frame();
        Check(saves == 1 && actionsX == initialActions + 52, "Escape restores saved width");
        Console.WriteLine("PASS Compact column long-press adjusts the actual table layout and saves only on release");

        windowWidth = 510;
        Frame(); Frame();
        Check(scrollX <= 1 && Math.Abs(tableRight - deathRight) <= 5 && actionWidth < wideAction && actionWidth >= 50,
            $"buttons fit a narrower window, death {deathRight}, table {tableRight}, scroll X {scrollX}, action {actionWidth}");
        rows = 20;
        Frame(); Frame();
        Check(scrollY > 0 && scrollX <= 1 && Math.Abs(tableRight - deathRight - ImGui.GetStyle().ScrollbarSize) <= 5,
            $"only the vertical scrollbar takes space, death {deathRight}, table {tableRight}, scroll X {scrollX}");
        windowWidth = 300;
        Frame(); Frame();
        Check(scrollX > 0 && actionWidth >= 50, $"tiny windows keep the buttons usable, scroll X {scrollX}, action {actionWidth}");
        Console.WriteLine("PASS Compact table fills the available width, adapts its button size, and scrolls only when needed");

        windowWidth = 700;
        rows = 4;
        showLocation = true;
        Frame(); Frame();
        float previousActionsX = actionsX;
        start = locationHandle;
        Point(start); Mouse(true); Hold(); Point(start + new Vector2(36, 0)); Frame();
        Check(saves == 1 && actionsX == previousActionsX + 36, "location width changes live without moving the monster handle");
        Mouse(false); Frame();
        Check(savedWidth == 172 && savedLocationWidth == 164 && saves == 2 && scrollX == 0 && Math.Abs(tableRight - deathRight) <= 5,
            "the location width saves independently and the table still reaches the right edge");
        windowWidth = 515; rows = 20; Frame(); Frame();
        Check(scrollY > 0 && scrollX == 0 && actionWidth >= 50 &&
            Math.Abs(tableRight - deathRight - ImGui.GetStyle().ScrollbarSize) <= 5,
            "narrower windows fit the location column together with all actions");
        windowWidth = 700; rows = 4; Frame(); Frame();
        Check(savedWidth == 172 && savedLocationWidth == 164 && actionsX == previousActionsX + 36,
            "widening the window restores the preferred text-column widths");

        Point(locationHeader); ImGui.GetIO().AddMouseButtonEvent(1, true); Frame();
        ImGui.GetIO().AddMouseButtonEvent(1, false); Frame();
        Check(columnMenuOpen, "right-clicking the location header opens the column menu");
        // Close the first menu and exercise the same entry point while the location column is hidden.
        Point(new Vector2(tableRight - 12, 250)); Mouse(true); Mouse(false); Frame();
        showLocation = false; Frame(); Frame();
        Point(monsterHeader); ImGui.GetIO().AddMouseButtonEvent(1, true); Frame();
        ImGui.GetIO().AddMouseButtonEvent(1, false); Frame();
        Check(columnMenuOpen && savedLocationWidth == 164, "right-clicking the monster header can restore the hidden location column");
        Point(new Vector2(tableRight - 12, 250)); Mouse(true); Mouse(false); Frame();
        Console.WriteLine("PASS Compact location column resizes independently and opens its visibility menu from both headers");
    }
}
