using HuntHelper.Gui.Native;
using System.Numerics;

internal static class NativeTrainChecks
{
    internal static readonly (string Name, Action Run)[] Tests = [
        ("native map groups preserve player map and monster order", GroupOrder),
        ("native map groups never merge separate instances", InstanceIsolation),
        ("native placeholders remain distinguishable from real dead records", Placeholders),
        ("native drag targets use screen origin and active window scale", ScreenDrag),
        ("native horizontal and vertical insertions use scaled target centers", DragDirection),
        ("native row drag scrolls at viewport edges and stops outside", EdgeScroll),
        ("native row menus retain exact world and record identity", RowMenu),
        ("native server deletion targets the right-clicked server", WorldMenu),
        ("native version clear retains the clicked version and server", VersionMenu),
    ];
    private static NativeTrainRow Row(string key, uint map, uint instance = 0, bool missing = false, bool dead = false) =>
        new(key, map, instance, $"地图{map}", key, missing, dead, false, !missing, !missing, "", "");
    private static NativeTrainSnapshot Snapshot(params NativeTrainRow[] rows) =>
        new(true, "A", 101, "测试服", false, false, false, false, "", [], [], rows);
    private static void Require(bool condition) { if (!condition) throw new Exception("Native train presentation contract failed"); }
    private static void GroupOrder()
    {
        var groups = Snapshot(Row("B1", 2), Row("A1", 1), Row("B2", 2)).Groups;
        Require(groups.Select(g => g.Territory).SequenceEqual(new uint[] { 2, 1 }));
        Require(groups[0].Rows.Select(r => r.Key).SequenceEqual(new[] { "B1", "B2" }));
    }
    private static void InstanceIsolation()
    {
        var groups = Snapshot(Row("1", 7, 1), Row("2", 7, 2), Row("3", 7, 1)).Groups;
        Require(groups.Count == 2 && groups[0].Rows.Count == 2 && groups[1].Rows.Count == 1);
        Require(groups[0].Title.EndsWith("1 线") && groups[1].Title.EndsWith("2 线"));
    }
    private static void Placeholders()
    {
        var snapshot = Snapshot(Row("alive", 1), Row("dead", 1, dead: true), Row("", 1, missing: true));
        Require(snapshot.Rows.Count(r => !r.Missing) == 2 && snapshot.Groups[0].Rows.Count == 3);
        Require(!snapshot.Rows[2].CanMove && !snapshot.Rows[2].CanTeleport && !snapshot.Rows[2].Dead);
    }
    private static void ScreenDrag()
    {
        foreach (var scale in new[] { .8f, 1f, 1.2f, 1.6f })
        {
            var origin = new Vector2(1280, 480); var size = new Vector2(150, 28);
            Require(NativeDragGeometry.Contains(origin + size * scale * .9f, origin, size, scale));
            Require(!NativeDragGeometry.Contains(new(20, 12), origin, size, scale));
            Require(!NativeDragGeometry.Contains(origin + new Vector2(size.X * scale + 1, 5), origin, size, scale));
        }
    }
    private static void DragDirection()
    {
        var origin = new Vector2(1400, 700); var size = new Vector2(150, 28);
        Require(!NativeDragGeometry.After(new(1450, 705), origin, size, 1.2f, false));
        Require(NativeDragGeometry.After(new(1560, 705), origin, size, 1.2f, false));
        Require(!NativeDragGeometry.After(new(1450, 710), origin, size, 1.2f, true));
        Require(NativeDragGeometry.After(new(1450, 730), origin, size, 1.2f, true));
    }
    private static void EdgeScroll()
    {
        var origin = new Vector2(1200, 500); var size = new Vector2(500, 300);
        Require(NativeDragGeometry.ScrollDirection(new(1300, 510), origin, size, 1.2f) == -1);
        Require(NativeDragGeometry.ScrollDirection(new(1300, 850), origin, size, 1.2f) == 1);
        Require(NativeDragGeometry.ScrollDirection(new(1300, 700), origin, size, 1.2f) == 0);
        Require(NativeDragGeometry.ScrollDirection(new(1300, 900), origin, size, 1.2f) == 0);
    }
    private static void RowMenu()
    {
        var menu = NativeTrainMenus.Row(101, "777/2");
        Require(menu.Select(x => x.Request.Action).SequenceEqual(new[] { NativeTrainAction.SelectOnly, NativeTrainAction.RowUp, NativeTrainAction.RowDown, NativeTrainAction.RemoveRow }));
        Require(menu.All(x => x.Request.World == 101 && x.Request.Key == "777/2"));
    }
    private static void WorldMenu()
    {
        var menu = NativeTrainMenus.Tab(101, 202, true);
        Require(menu[0].Request.Action == NativeTrainAction.World && menu[0].Request.World == 202);
        Require(menu[3].Request.Action == NativeTrainAction.DeleteWorld && menu[3].Request.Key == "202" && menu[3].Request.World == 101);
    }
    private static void VersionMenu()
    {
        var menu = NativeTrainMenus.Tab(101, 7, false);
        Require(menu[3].Request.Action == NativeTrainAction.ClearVersion && menu[3].Request.Key == "7" && menu[3].Request.World == 101);
        Require(!menu[1].Request.Value && menu[2].Request.Value);
    }
}
