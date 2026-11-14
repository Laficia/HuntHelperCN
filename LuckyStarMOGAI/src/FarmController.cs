// Based on NiGuangOwO/LuckyStar; modified 2026-11-13. AGPL-3.0-or-later.
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ECommons.DalamudServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace LuckyStar;

public sealed unsafe class FarmController : IDisposable
{
    private readonly GameFarmHost host = new();
    public FarmSession Session { get; }
    public RubyPatrol Ruby { get; }
    private bool rubyMode;
    public bool IsRunning => Session.Running || Ruby.Running;
    private string cleanupError="";
    public bool CleanupFailed=>cleanupError.Length>0;
    public string State => (rubyMode ? Ruby.State : Session.State)+(cleanupError.Length>0?"；"+cleanupError:"");
    public bool ShowingRuby => rubyMode;
    public IReadOnlyDictionary<uint,int> Deaths => rubyMode ? Ruby.Deaths : Session.Deaths;
    public FarmProfile? ActiveProfile { get; private set; }
    private FarmProgress Progress => rubyMode ? Ruby.Progress : Session.Progress;
    private long nextUpdate;
    public FarmController() { Session = new(host); Ruby = new(host); Svc.Framework.Update += Update; }
    public void Dispose() { Svc.Framework.Update -= Update; try { Stop("插件已卸载"); } finally { host.Dispose(); } }
    public string CombatAvailability => host.Rotation.Availability(Plugin.Configuration.Rotation);
    public string CombatStatus => host.Rotation.Message;
    public string StatusJson()
    {
        var c=host.Context;
        return JsonSerializer.Serialize(new{Running=IsRunning,State,Version=typeof(FarmController).Assembly.GetName().Version?.ToString(),
            Phase=rubyMode?Ruby.Phase.ToString():"Farm",Ruby.CaveIndex,Ruby.CompletedVisits,Rotation=Plugin.Configuration.Rotation.ToString(),CombatStatus,
            Position=new{c.Position.X,c.Position.Y,c.Position.Z},c.Scope,SessionScope=rubyMode?Ruby.BoundScope:Session.BoundScope,
            FarmIndex=Plugin.Configuration.SelectedFarm,Names=ActiveProfile?.Names,
            c.Mounted,c.Flying,c.Diving,c.Combat,c.Ready,EngageDistance=host.EngageDistance,Deaths,Navigation=rubyMode?Ruby.NavigationDiagnostics:Session.NavigationDiagnostics});
    }
    public void Stop(string reason = "已停止")
    {
        try { try { Session.Stop(reason); } finally { Ruby.Stop(reason); } }
        catch (Exception ex) { Svc.Log.Error(ex, "LuckyStarMOGAI stop failed"); }
        finally { EndRotation(); }
    }
    public bool Start(FarmProfile profile, IEnumerable<Vector3> points,FarmCheckpoint? checkpoint=null)
    {
        if(IsRunning) return false;
        rubyMode=profile.Ground;
        cleanupError="";
        Session.DelaySeconds = Plugin.Configuration.DelayEnable ? Plugin.Configuration.DelayTime : 0;
        Ruby.DelaySeconds = Session.DelaySeconds;
        Session.MountBetweenTargets=Ruby.MountBetweenTargets=Plugin.Configuration.MountBetweenTargets;
        Session.FlyBetweenTargets=Ruby.FlyBetweenTargets=Plugin.Configuration.FlyBetweenTargets;
        ActiveProfile=profile;
        try
        {
            host.BeginTick();
            if(checkpoint!=null && (checkpoint.Scope.Territory!=profile.Territory || !checkpoint.Matches(host.Context.Scope,Plugin.Configuration.SelectedFarm,profile.Names,DateTimeOffset.UtcNow)))
                throw new InvalidOperationException("进度记录已过期，或服务器、地图、分线、农怪项目已变化");
            var error=CombatAvailability;if(error.Length>0)throw new InvalidOperationException(error);
            var started=rubyMode ? Ruby.Start(profile,Plugin.Configuration.RubyCaves,Environment.TickCount64) : Session.Start(profile, points, Environment.TickCount64);
            if(!started)return false;
            if(checkpoint!=null)Progress.RestoreDeaths(checkpoint.Deaths);
            host.Rotation.Begin(Plugin.Configuration.Rotation,Plugin.Configuration.AllowRotationStart);
            return true;
        }
        catch (Exception ex) { Stop("启动失败："+ex.Message); Svc.Log.Error(ex, "LuckyStarMOGAI start failed"); return false; }
    }
    public List<Vector3> RubyPoints(uint[] names)
    {
        var c = host.Context;
        return host.Mobs.Where(m => names.Contains(m.NameId) && m.Hp > 0 && FarmSession.Finite(m.Position) &&
            Vector3.Distance(m.Position, c.Position) <= 120 && Math.Abs(m.Position.Y - c.Position.Y) <= 18)
            .Select(m => m.Position).Distinct().Take(80).ToList();
    }
    private void Update(IFramework framework)
    {
        if (!IsRunning || Environment.TickCount64 < nextUpdate) return;
        nextUpdate = Environment.TickCount64 + 50;
        try
        {
            host.BeginTick();
            var context=host.Context;
            if(context.Ready && !context.Busy && host.Rotation.CheckWhileReady() is { } error){Stop(error);return;}
            if(Ruby.Running) Ruby.Tick(Environment.TickCount64); else Session.Tick(Environment.TickCount64);
        }
        catch (Exception ex) { Stop("运行中断："+ex.Message); Svc.Log.Error(ex, "LuckyStarMOGAI farm tick failed"); }
        finally { if(!IsRunning)EndRotation(); }
        try
        {
            foreach(var name in Progress.TakeCompletedKinds())
                Notify($"{MobName(name)} 数量已完成（{Deaths.GetValueOrDefault(name)}/{ActiveProfile?.Goals?.GetValueOrDefault(name)}），将跳过该种类");
            if(!IsRunning && (rubyMode ? Ruby.Completed : Session.Completed))
                Notify($"{ActiveProfile?.Name}：已勾选小怪数量目标全部完成，已停止农怪。S 怪是否刷新仍受冷却等条件影响。");
            else if(!IsRunning && State.StartsWith("发现"))Notify(State);
        }
        catch(Exception ex){Svc.Log.Error(ex,"LuckyStarMOGAI notification failed");}
    }
    public static string MobName(uint name)
    {
        for(var i=0;i<FarmProfiles.NameIds.Length;i++)
        {var at=Array.IndexOf(FarmProfiles.NameIds[i],name);if(at>=0)return FarmProfiles.Labels[i][at];}
        return name.ToString();
    }
    private static void Notify(string message)
    {
        Svc.Chat.Print("[LuckyStarMOGAI] "+message);
        Svc.Toasts.ShowNormal(message);
    }
    private void EndRotation()
    {
        try{host.Rotation.End();cleanupError="";}
        catch(Exception ex){cleanupError="循环释放失败，请再次点停止或在所选插件中检查状态";Svc.Log.Error(ex,cleanupError);}
        finally{host.StopOwnedAttack();}
    }
}

internal sealed unsafe class GameFarmHost : IFarmHost, IDisposable
{
    private IReadOnlyList<FarmMob>? tickMobs;
    public void BeginTick()=>tickMobs=null;
    private readonly GameRotationApi rotationApi = new();
    public CombatRotationSession Rotation { get; }
    public GameFarmHost()=>Rotation=new(rotationApi);
    public void Dispose(){try{Rotation.Dispose();}finally{StopOwnedAttack();rotationApi.Dispose();}}
    public float EngageDistance=>Rotation.Provider==RotationProvider.AE || Svc.Objects.LocalPlayer?.ClassJob.Value.Role is 3 or 4 ? 25 : 3;
    public bool ManagesCombatMovement=>Rotation.Provider!=RotationProvider.AE;
    public FarmContext Context
    {
        get
        {
            var p = Svc.Objects.LocalPlayer;
            var loading = Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51] ||
                Svc.GameGui.GetAddonByName("NowLoading",1).IsVisible || Svc.GameGui.GetAddonByName("FadeMiddle",1).IsVisible || Svc.GameGui.GetAddonByName("FadeBack",1).IsVisible;
            var ready = Svc.ClientState.IsLoggedIn && p != null && !loading;
            return new(ready, new(p?.CurrentWorld.RowId ?? 0, Svc.ClientState.TerritoryType, UIState.Instance()->PublicInstance.InstanceId),
                p?.Position ?? Vector3.Zero, p?.CurrentHp == 0,
                Svc.Condition[ConditionFlag.OccupiedInCutSceneEvent] || Svc.Condition[ConditionFlag.WatchingCutscene78],
                Svc.Condition[ConditionFlag.InCombat], Svc.Condition[ConditionFlag.Mounted], Svc.Condition[ConditionFlag.InFlight],
                Svc.Condition[ConditionFlag.Diving], loading, Svc.ClientState.IsLoggedIn, p?.IsCasting ?? false);
        }
    }
    public IReadOnlyList<FarmMob> Mobs => tickMobs ??= Svc.Objects.OfType<IBattleChara>()
        .Where(b => b.NameId is > 0 && b.EntityId != 0xE0000000)
        .Select(b => new FarmMob(b.GameObjectId, b.NameId, b.Position, b.CurrentHp, b.MaxHp, b.HitboxRadius)).ToList();
    public bool NavigationReady => Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Nav.IsReady") is { HasFunction: true } gate && gate.InvokeFunc();
    public bool NavigationBusy => Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress").InvokeFunc() ||
        Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning").InvokeFunc();
    public bool MovementAllowed => Svc.PluginInterface.GetIpcSubscriber<bool>("vnavmesh.Path.GetMovementAllowed") is { HasFunction: true } gate && gate.InvokeFunc();
    public bool Move(Vector3 p, bool fly) => Svc.PluginInterface.GetIpcSubscriber<Vector3, bool, bool>("vnavmesh.SimpleMove.PathfindAndMoveTo").InvokeFunc(p, fly);
    public Vector3? GroundPoint(Vector3 position)
    {
        var gate=Svc.PluginInterface.GetIpcSubscriber<Vector3,bool,float,Vector3?>("vnavmesh.Query.Mesh.PointOnFloor");
        return gate.HasFunction ? gate.InvokeFunc(position+new Vector3(0,3,0),false,1) : null;
    }
    public bool MoveDirect(Vector3 p,bool fly)
    {
        // Short preset portal corridor only. Native vnavmesh executor/settings, no coordinate writes.
        var gate=Svc.PluginInterface.GetIpcSubscriber<List<Vector3>,bool,object>("vnavmesh.Path.MoveTo");
        if(!gate.HasAction) return false;
        gate.InvokeAction([p],fly);return true;
    }
    public void StopMovement()
    {
        // Dispatch to the loaded CN plugin's own command handler; cancels pending calculation too.
        if (!Svc.Commands.ProcessCommand("/vnav stop"))
            Svc.PluginInterface.GetIpcSubscriber<object>("vnavmesh.Path.Stop").InvokeAction();
    }
    public void SetCombat(bool on)
    {
        try{Rotation.SetCombat(on);}finally{if(!on)StopOwnedAttack();}
    }
    public void Focus(ulong id)
    {
        var b = Svc.Objects.FirstOrDefault(b => b.GameObjectId == id);
        if (b != null){focusedTarget=id;if(Svc.Targets.Target?.GameObjectId != id)Svc.Targets.Target = b;}
        if(b is IBattleChara battle && battle.CurrentHp>0 && Rotation.Provider!=RotationProvider.AE &&
            !Svc.Condition[ConditionFlag.InCombat] && !UIState.Instance()->WeaponState.AutoAttackState.IsAutoAttacking &&
            Svc.Objects.LocalPlayer is { } player && Vector3.Distance(player.Position,b.Position)<=3 && Environment.TickCount64>=nextAttack)
        {
            nextAttack=Environment.TickCount64+2000;
            if(ActionManager.Instance()->UseAction(ActionType.GeneralAction,1)) ownsAutoAttack=true;
        }
    }
    private ulong focusedTarget;
    public void ClearTarget()
    {
        if(focusedTarget!=0 && Svc.Targets.Target?.GameObjectId==focusedTarget)Svc.Targets.Target=null;
        focusedTarget=0;
    }
    private long nextAttack;
    private bool ownsAutoAttack;
    public void StopOwnedAttack()
    {
        if(!ownsAutoAttack)return;
        ownsAutoAttack=false;
        if(Svc.Objects.LocalPlayer!=null && UIState.Instance()->WeaponState.AutoAttackState.IsAutoAttacking)
            ActionManager.Instance()->UseAction(ActionType.GeneralAction,1);
    }
    public void Mount() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 9);
    public void TakeOff() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 2);
    public void Dismount() => ActionManager.Instance()->UseAction(ActionType.GeneralAction, 23);
}



