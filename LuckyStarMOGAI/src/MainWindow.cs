// Modified from NiGuangOwO/LuckyStar MainWindow, 2026-11-14. AGPL-3.0-or-later.
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using ECommons.DalamudServices;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text.Json;
namespace LuckyStar;
public sealed class MainWindow : Window, IDisposable
{
    private readonly FarmController controller = new();
    private static readonly int[] RowOrder = [1,2,3,0,4,5,6];
    private static readonly string[] RowTitles = ["红玉海 - 巨大鳐", "萨维奈岛 - 颇胝迦", "叹息海 - 沉思之物", "拉凯提亚大森林 - 伊休妲", "基拉巴尼亚边区 - 优昙婆罗花", "魔大陆阿济兹拉 - 卢克洛塔", "北萨纳兰 - 蚓螈巨虫"];
    public MainWindow() : base("LuckyStar###LuckyStarMOGAI", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        Size = new Vector2(600,460); SizeCondition=ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize=new(540,400), MaximumSize=new(float.MaxValue,float.MaxValue) };
    }
    public void Dispose()=>controller.Dispose();
    public void Stop()=>controller.Stop();
    public string StatusJson()=>controller.StatusJson();
    public bool ResumeFromCheckpoint(string json)
    {
        if(json.Length>8192)return false;
        try{return JsonSerializer.Deserialize<FarmCheckpoint>(json) is { } checkpoint && StartSelected(checkpoint);}
        catch(JsonException){return false;}
    }
    public bool StartSelected(FarmCheckpoint? checkpoint=null)
    {
        var config=Plugin.Configuration;
        var profile=FarmProfiles.Profile(config.SelectedFarm,config.Selections[config.SelectedFarm]);
        var points=config.SelectedFarm==0 ? controller.RubyPoints(profile.Names) : FarmProfiles.Preset(config.SelectedFarm,config.Selections[config.SelectedFarm]);
        var started=controller.Start(profile,points,checkpoint);
        var message=started ? "农怪已启动："+controller.State : "无法开始农怪："+controller.State;
        if(started){Svc.Chat.Print("[LuckyStarMOGAI] "+message);Svc.Toasts.ShowNormal(message);}
        else{Svc.Chat.PrintError("[LuckyStarMOGAI] "+message);Svc.Toasts.ShowError(message);Svc.Log.Warning(message);}
        return started;
    }
    public override void Draw()
    {
        DrawTopBar();
        if(!ImGui.BeginTabBar("mainTab"))return;
        if(ImGui.BeginTabItem("农怪")){DrawFarmTab();ImGui.EndTabItem();}
        if(Plugin.Configuration.DevMode && ImGui.BeginTabItem("Debug")){DrawDebugTab();ImGui.EndTabItem();}
        ImGui.EndTabBar();
    }
    private void DrawTopBar()
    {
        var config=Plugin.Configuration;
        if(ImGui.Button("停止"))controller.Stop();
        ImGui.SameLine();
        if(controller.IsRunning)ImGui.TextColored(ImGuiColors.HealerGreen,$"状态：{controller.State}");
        else ImGui.Text($"状态：{controller.State}");
        ImGui.BeginDisabled(controller.IsRunning);
        if(ImGui.Checkbox("##DelayEnable",ref config.DelayEnable))config.Save();
        ImGui.SameLine();ImGui.Text("每轮第一只刷新时延迟下坐骑");
        if(config.DelayEnable)
        {
            ImGui.SameLine();ImGui.SetNextItemWidth(100);
            if(ImGui.InputInt("延迟时间 (s)",ref config.DelayTime))
            {config.DelayTime=Math.Clamp(config.DelayTime,0,120);config.Save();}
        }
        ImGui.SameLine();ImGui.SetNextItemWidth(Math.Clamp(ImGui.GetContentRegionAvail().X,80,180));
        var rotation=(int)config.Rotation;
        if(ImGui.Combo("##循环插件",ref rotation,CombatRotationSession.Names,CombatRotationSession.Names.Length))
        {config.Rotation=(RotationProvider)rotation;config.Save();}
        if(ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.BeginTooltip();ImGui.TextUnformatted("选择使用的循环插件");
            if(controller.CombatAvailability is {Length:>0} reason)ImGui.TextUnformatted(reason);
            ImGui.EndTooltip();
        }
        ImGui.EndDisabled();
        if(ImGui.Checkbox("##DevMode",ref config.DevMode))config.Save();
        ImGui.SameLine();ImGui.Text("开发者模式");
    }
    private void DrawFarmTab()
    {
        for(var row=0;row<RowOrder.Length;row++)
        {if(row>0)ImGui.Separator();DrawFarmRow(RowOrder[row]);}
    }
    private void DrawFarmRow(int farm)
    {
        var config=Plugin.Configuration;
        ImGui.PushID(farm);
        ImGui.TextUnformatted(RowTitles[farm]);ImGui.SameLine();
        ImGui.BeginDisabled(controller.IsRunning);
        // The original worm row has only Go and no child checkbox.
        if(farm!=6)
        {
            var choices=config.Selections[farm];
            for(var i=0;i<choices.Length;i++)
            {if(ImGui.Checkbox(FarmProfiles.Labels[farm][i],ref choices[i]))config.Save();ImGui.SameLine();}
        }
        if(ImGui.Button("Go"))
        {
            config.SelectedFarm=farm;
            if(farm==6)config.Selections[farm][0]=true;
            config.Save();StartSelected();
        }
        if(farm==0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("红玉海三个洞穴循环；可从玉水村、农怪洞穴或海底开始。\n洞穴选择与骑乘设置保留在开发者模式的运行设置中。");
        ImGui.EndDisabled();ImGui.PopID();
    }
    private void DrawDebugTab()
    {
        if(!ImGui.BeginChild("DebugContent",Vector2.Zero)){ImGui.EndChild();return;}
        var player=Svc.Objects.LocalPlayer;
        ImGui.Text($"Position: {player?.Position}");
        if(ImGui.Button("复制坐标"))ImGui.SetClipboardText($"({player?.Position.X}f,{player?.Position.Y}f,{player?.Position.Z}f),");
        ImGui.Separator();ImGui.Text($"IsRunning: {controller.IsRunning}");ImGui.Text($"状态：{controller.State}");
        if(controller.ShowingRuby)
        {
            ImGui.Text($"洞穴: {RubyCaves.All[controller.Ruby.CaveIndex].Name}");
            ImGui.Text($"Phase: {controller.Ruby.Phase}");ImGui.Text($"CompletedVisits: {controller.Ruby.CompletedVisits}");
        }
        else ImGui.Text($"DataIndex: {controller.Session.Index} / {controller.Session.Points.Count}");
        if(controller.ActiveProfile is { } active)
        {
            ImGui.Separator();ImGui.TextUnformatted(active.Name);
            foreach(var name in active.Names)
                ImGui.Text($"{FarmController.MobName(name)}：{controller.Deaths.GetValueOrDefault(name)}/{active.Goals?.GetValueOrDefault(name)}");
        }
        if(ImGui.CollapsingHeader("运行设置"))DrawRunSettings();
        if(ImGui.CollapsingHeader("关于与许可证"))
        {
            ImGui.TextWrapped("原作 NiGuangOwO/LuckyStar，定制 Laficia。AGPL-3.0-or-later；完整许可随包附带 LICENSE.md，源码随发行版提供。");
            ImGui.TextUnformatted("https://github.com/Laficia/HuntHelperCN");
        }
        ImGui.EndChild();
    }
    private void DrawRunSettings()
    {
        var config=Plugin.Configuration;
        ImGui.BeginDisabled(controller.IsRunning);
        if(config.Rotation is RotationProvider.RotationSolverReborn or RotationProvider.Prome)
            if(ImGui.Checkbox("一键托管循环（自动开启，停止时释放）",ref config.AllowRotationStart))config.Save();
        if(ImGui.Checkbox("小怪之间自动上坐骑（潜水也骑乘）",ref config.MountBetweenTargets))config.Save();
        if(config.MountBetweenTargets && ImGui.Checkbox("骑乘使用飞行寻路（包含洞内）",ref config.FlyBetweenTargets))config.Save();
        ImGui.TextUnformatted("红玉海洞穴：");
        for(var i=0;i<RubyCaves.All.Length;i++)if(ImGui.Checkbox(RubyCaves.All[i].Name,ref config.RubyCaves[i]))config.Save();
        ImGui.EndDisabled();
        ImGui.TextColored(ImGuiColors.DalamudYellow,FarmProfiles.EorzeaDate(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        ImGui.TextWrapped("观梦螺、无壳观梦螺各 100；还受满月和 S 怪冷却影响。数量完成不代表确认 S 怪刷新。本次只统计本机观测死亡，重新开始清零。");
        ImGui.TextWrapped("需要国服 vnavmesh 和配置好的循环插件。请勿同时运行原 LuckyStar 或 HHT 找怪。Wrath 严格限定目标时需关闭 AoEIgnoreManual。");
        if(controller.CombatAvailability is {Length:>0} reason)ImGui.TextWrapped(reason);
        ImGui.TextWrapped(controller.CombatStatus);
    }
}
