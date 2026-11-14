// Modified from NiGuangOwO/LuckyStar MainWindow, 2026-11-13. AGPL-3.0-or-later.
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
    public MainWindow() : base("LuckyStarMOGAI 0.0.0.9###LuckyStarMOGAI")
    {
        Size = new Vector2(620,620); SizeCondition=ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize=new(320,260), MaximumSize=new(float.MaxValue,float.MaxValue) };
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
        var config=Plugin.Configuration;
        ImGui.PushStyleColor(ImGuiCol.Text,controller.IsRunning ? ImGuiColors.HealerGreen : ImGuiColors.DalamudWhite);
        ImGui.TextWrapped($"状态：{controller.State}");ImGui.PopStyleColor();
        ImGui.BeginDisabled(controller.IsRunning);
        if(ImGui.Button("开始农怪",new Vector2(-1,0)))StartSelected();
        ImGui.EndDisabled();
        if((controller.IsRunning || controller.CleanupFailed) && ImGui.Button("停止农怪")) controller.Stop();
        ImGui.Separator();
        ImGui.BeginDisabled(controller.IsRunning);
        var rotation=(int)config.Rotation;
        ImGui.SetNextItemWidth(Math.Max(150,ImGui.GetContentRegionAvail().X-90));
        if(ImGui.Combo("自动循环",ref rotation,CombatRotationSession.Names,CombatRotationSession.Names.Length))
        { config.Rotation=(RotationProvider)rotation;config.Save(); }
        if(config.Rotation is RotationProvider.RotationSolverReborn or RotationProvider.Prome)
        {
            if(ImGui.Checkbox("一键托管循环（自动开启，停止时释放）",ref config.AllowRotationStart))config.Save();
            ImGui.TextWrapped(config.AllowRotationStart ? "点一次开始，自动巡怪、靠近、开启循环、击杀后继续巡逻；已开启的循环保持原设置，临时开启的循环离开点位／停止时关闭。托管中请先停止农怪再改循环模式。" : "手动模式：请先在所选插件中开启循环；循环停止后农怪也会停止。勾选一键托管可自动衔接后续战斗。");
        }
        if(controller.CombatAvailability is { Length:>0 } unavailable)
        {ImGui.PushStyleColor(ImGuiCol.Text,ImGuiColors.DalamudYellow);ImGui.TextWrapped(unavailable);ImGui.PopStyleColor();}
        if(ImGui.Checkbox("小怪之间自动上坐骑（潜水也骑乘）",ref config.MountBetweenTargets))config.Save();
        if(config.MountBetweenTargets && ImGui.Checkbox("骑乘使用飞行寻路（包含洞内）",ref config.FlyBetweenTargets))config.Save();
        ImGui.TextWrapped("脱战后骑乘寻路，接近开怪距离并落地后下坐骑；当前目标已在开怪范围内时直接击杀。每种小怪达到数量目标后自动跳过，已勾选种类全部完成后停止并通知。");
        var selected=config.SelectedFarm;
        ImGui.SetNextItemWidth(-1);
        if(ImGui.Combo("##农怪项目",ref selected,FarmProfiles.Titles,FarmProfiles.Titles.Length)) {config.SelectedFarm=selected;config.Save();}
        var choices=config.Selections[selected];
        for(var i=0;i<choices.Length;i++)
        {
            if(i>0 && ImGui.GetContentRegionAvail().X>180) ImGui.SameLine();
            if(ImGui.Checkbox($"{FarmProfiles.Labels[selected][i]}（{FarmProfiles.KillGoal(selected)}只）",ref choices[i])) config.Save();
        }
        if(selected==0)
        {
            ImGui.Spacing();
            for(var i=0;i<3;i++) if(ImGui.Checkbox($"{RubyCaves.All[i].Name} ({RubyCaves.All[i].Map.X:0.#}, {RubyCaves.All[i].Map.Y:0.#})",ref config.RubyCaves[i])) config.Save();
            ImGui.TextWrapped("可从玉水村开始，自动出村下潜并前往洞穴；也可在任一目标洞穴或海底开始。洞内骑乘飞行巡怪，洞间骑乘潜水，接近目标后下坐骑击杀。");
            ImGui.TextColored(ImGuiColors.DalamudYellow,FarmProfiles.EorzeaDate(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
            ImGui.TextWrapped("触发参考：两类各 100 只；满月 ET 第 16 日 12:00 起。还受 S 怪重生冷却影响，不保证触发。");
        }
        else ImGui.TextWrapped("使用原插件预设点位，上坐骑、起飞、巡点；请先到对应地图并开启飞行。");
        if(Svc.ClientState.TerritoryType!=FarmProfiles.Territories[selected])
            ImGui.TextColored(ImGuiColors.DalamudYellow,"请先前往该项目对应地图。");
        ImGui.EndDisabled();
        if(controller.ActiveProfile is { } active)
        {
            ImGui.Separator();
            ImGui.TextWrapped($"本次项目：{active.Name}");
            foreach(var name in active.Names)
            {
                var count=controller.Deaths.GetValueOrDefault(name);var goal=active.Goals?.GetValueOrDefault(name)??0;
                ImGui.TextWrapped($"{FarmController.MobName(name)}：{count}/{goal}"+(goal>0 && count>=goal?"  已完成，跳过":""));
            }
            ImGui.TextWrapped("只统计本机观测的存活 → 死亡，重新开始清零。数量完成会通过聊天与屏幕提示通知；不代表全服进度或确认 S 怪刷新。未勾选种类不纳入本次完成判断。");
        }
        ImGui.Spacing();
        if(ImGui.CollapsingHeader("选项与依赖"))
        {
            ImGui.BeginDisabled(controller.IsRunning);
            if(ImGui.Checkbox("每轮首只小怪延迟",ref config.DelayEnable)) config.Save();
            if(config.DelayEnable && ImGui.SliderInt("秒",ref config.DelayTime,0,120)) config.Save();
            ImGui.EndDisabled();
            ImGui.TextWrapped("需要国服 vnavmesh（API 15），自动循环可选 AE、RSR、Wrath 或 Prome。请在所选插件中先配置当前职业。Wrath 使用临时租约，停止时释放；严格限定目标时请在 Wrath 关闭 AoEIgnoreManual（群体忽略手动目标）。");
            ImGui.TextWrapped("RSR 需要 AutorotationActive / ChangeOperatingMode 接口，部分旧版不提供。Prome 保留自身验证与 ACR 要求，不改 AutoPull 设置。缺少接口或拒绝启动时会明确停止。");
            ImGui.TextWrapped("达到数量后不再主动选中该种类；循环引擎的群攻仍可能附带击杀。需要严格按目标攻击时，请让 RSR 从关闭状态交由本插件开启，并关闭所选引擎自身的自由选怪。AE 使用本插件指定目标拉怪。");
            if(controller.IsRunning)ImGui.TextWrapped(controller.CombatStatus);
            ImGui.TextWrapped("切服、切图、切分线、死亡、导航中断和卡住会停止。也可使用 /luckystarmogai stop；不会修改 vnavmesh 的手动操控、镜头或容差设置。请勿同时运行原 LuckyStar 或 HHT 找怪。");
        }
        if(ImGui.CollapsingHeader("关于与许可证"))
        {
            ImGui.TextWrapped("原作 NiGuangOwO/LuckyStar，定制修改 Laficia（2026-11-13）。AGPL-3.0-or-later；无担保，可按该许可证再分发与修改。完整许可随包附带 LICENSE.md，对应源码见仓库 LuckyStarMOGAI-source.zip。");
            ImGui.TextWrapped("https://github.com/Laficia/HuntHelperCN");
        }
    }
}


