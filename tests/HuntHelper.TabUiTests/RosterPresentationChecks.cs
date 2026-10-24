using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using System.Numerics;

internal static class RosterPresentationChecks
{
    public static void Run()
    {
        ImGui.NewFrame(); ImGui.SetNextWindowPos(Vector2.Zero); ImGui.SetNextWindowSize(new Vector2(600,300));
        ImGui.Begin("RosterPresentationTest",ImGuiWindowFlags.NoSavedSettings);
        var rounding=ImGui.GetStyle().FrameRounding;var frame=ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg];
        HuntRosterPresentation.Summary("World",7,3);
        var y=ImGui.GetCursorPosY();HuntRosterPresentation.Status(false,false,1);
        if(ImGui.GetCursorPosY()<=y || ImGui.GetStyle().FrameRounding!=rounding || ImGui.GetStyle().Colors[(int)ImGuiCol.FrameBg]!=frame)
            throw new Exception("Roster rendering leaked styles or collapsed status row height");
        HuntRosterPresentation.Status(true,false,1);HuntRosterPresentation.Status(true,true,1);
        ImGui.End();ImGui.Render();
        Console.WriteLine("PASS Native recorded-count summary and status badges render without leaking global styles");
    }
}
