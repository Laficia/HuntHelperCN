using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;

internal static class WorldPickerChecks
{
    internal static readonly IReadOnlyList<HuntTrainWorld> Worlds=HuntWorldCatalog.Select(
        JsonSerializer.Deserialize<List<HuntWorldCatalogEntry>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"CnWorldCatalog.json")))!,1183,true);
    private static readonly HuntWorldPicker Picker=new();
    private static readonly List<uint> Order=new();
    private static void Check(bool ok){if(!ok)throw new Exception("Native world picker assertion failed");}
    private static void Frame()
    {
        ImGui.NewFrame();ImGui.SetNextWindowPos(Vector2.Zero);ImGui.SetNextWindowSize(new(700,390));
        ImGui.Begin("WorldPickerChecks",ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoTitleBar);
        Picker.Draw(Worlds,1183,Order);ImGui.End();ImGui.Render();
    }
    private static void Click((Vector2 Min,Vector2 Max) bounds)
    {
        var p=(bounds.Min+bounds.Max)/2;var io=ImGui.GetIO();io.AddMousePosEvent(p.X,p.Y);Frame();
        io.AddMouseButtonEvent(0,true);Frame();io.AddMouseButtonEvent(0,false);Frame();Frame();
    }
    private static void Group(string name){Click(Picker.ComboBounds);Check(Picker.GroupBounds.ContainsKey(name));Click(Picker.GroupBounds[name]);}
    public static void Run()
    {
        Frame();Frame();Check(Picker.DataCenter=="豆豆柴" && Picker.VisibleWorlds.Count==6 && Picker.WorldBounds.Count==6);
        Console.WriteLine("PASS Native CN selector renders current data center and all six Doudouchai servers");
        Click(Picker.ComboBounds);Check(Picker.GroupBounds.Count==5 && new[]{"陆行鸟","莫古力","猫小胖","豆豆柴",""}.All(Picker.GroupBounds.ContainsKey));
        Click(Picker.GroupBounds["陆行鸟"]);Check(Picker.VisibleWorlds.Count==8);
        Console.WriteLine("PASS Native data-center dropdown exposes all four CN groups and All");
        Click(Picker.WorldBounds[1044]);Click(Picker.WorldBounds[1042]);Check(Order.SequenceEqual(new uint[]{1044,1042}));
        Group("豆豆柴");Click(Picker.WorldBounds[1183]);Check(Order.SequenceEqual(new uint[]{1044,1042,1183}));
        Click(Picker.WorldBounds[1183]);Click(Picker.WorldBounds[1201]);Click(Picker.WorldBounds[1183]);Check(Order.SequenceEqual(new uint[]{1044,1042,1201,1183}));
        Console.WriteLine("PASS Native checkbox order survives data-center filtering and rechecking appends to the queue");
    }
}
