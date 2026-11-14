using Dalamud.Bindings.ImGui;
using HuntHelper.Gui;
using System.Numerics;

internal static class WorldPickerPreview
{
    public static void Render()
    {
        Preview(false,"server-list.png");
        Preview(true,"data-center-menu.png");
    }
    private static void Preview(bool menu,string name)
    {
        var picker=new HuntWorldPicker();var order=new List<uint>{1183,1201,1192};var frame=0;
        RosterPreviewRenderer.RenderWidget("obj/previews/server-picker/"+name,720,480,()=>
        {
            var io=ImGui.GetIO();
            if(menu && frame==2){var p=(picker.ComboBounds.Min+picker.ComboBounds.Max)/2;io.AddMousePosEvent(p.X,p.Y);}
            if(menu && frame==3)io.AddMouseButtonEvent(0,true);
            if(menu && frame==4)io.AddMouseButtonEvent(0,false);
            ImGui.NewFrame();
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding,new Vector2(18,14));
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing,new Vector2(8,8));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding,4);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(7,4));
            ImGui.PushStyleColor(ImGuiCol.WindowBg,new Vector4(.085f,.10f,.14f,1));
            ImGui.PushStyleColor(ImGuiCol.ChildBg,new Vector4(.10f,.12f,.17f,1));
            ImGui.PushStyleColor(ImGuiCol.FrameBg,new Vector4(.16f,.18f,.24f,1));
            ImGui.PushStyleColor(ImGuiCol.CheckMark,new Vector4(.35f,.65f,1,1));
            ImGui.PushStyleColor(ImGuiCol.Header,new Vector4(.20f,.38f,.62f,1));
            ImGui.SetNextWindowPos(new(16,16));ImGui.SetNextWindowSize(new(688,448));
            ImGui.Begin("多服务器找怪 · 界面预览##WorldPicker"+name,ImGuiWindowFlags.NoResize|ImGuiWindowFlags.NoSavedSettings|ImGuiWindowFlags.NoCollapse);
            var enabled=true;ImGui.Checkbox("多服务器找怪（按勾选顺序）",ref enabled);
            ImGui.TextDisabled("当前区服：银泪湖    ·    需要国服 API 15 版 Lifestream");
            ImGui.Separator();ImGui.Spacing();
            picker.Draw(WorldPickerChecks.Worlds,1183,order);
            ImGui.Spacing();ImGui.Separator();ImGui.TextDisabled("本机国服名单 · 勾选为演示 · 尚未部署");
            ImGui.End();ImGui.PopStyleColor(5);ImGui.PopStyleVar(4);ImGui.Render();frame++;
        });
    }
}
