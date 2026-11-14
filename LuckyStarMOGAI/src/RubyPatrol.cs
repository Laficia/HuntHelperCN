// LuckyStarCN, 2026-11-13. AGPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
namespace LuckyStar;

public enum RubyPhase { Scan, Farm, ExitApproach, Exit, Approach, Enter, TownExitApproach, TownExit }

/// <summary>Three native cave transitions; loading suspends clocks and issues no movement/combat requests.</summary>
public sealed class RubyPatrol(IFarmHost host)
{
    public bool Running { get; private set; }
    public string State { get; private set; } = "未运行";
    public RubyPhase Phase { get; private set; }
    public FarmScope BoundScope=>_scope;
    public int CaveIndex { get; private set; }
    public int CompletedVisits { get; private set; }
    public FarmProgress Progress { get; } = new();
    public Dictionary<uint,int> Deaths => Progress.Deaths;
    public bool Completed { get; private set; }
    private FarmSession? farm;
    private FarmSession _farm => farm ??= new(host,Progress);
    public object NavigationDiagnostics=>_farm.NavigationDiagnostics;
    public bool MountBetweenTargets { get => _farm.MountBetweenTargets; set => _farm.MountBetweenTargets=value; }
    public bool FlyBetweenTargets { get => _farm.FlyBetweenTargets; set => _farm.FlyBetweenTargets=value; }
    private readonly FarmTravel travel = new(host);
    public int DelaySeconds { get => _farm.DelaySeconds; set => _farm.DelaySeconds=value; }
    private FarmScope _scope;
    private FarmProfile _profile = null!;
    private int[] _caves = [];
    private bool _moving, _wasLoading, _defending;
    private bool awaitingSceneReady;
    private long? sceneReadySince;
    private long _defendAt;
    private uint defenseName;
    private long _phaseAt, _moveAt, _progressAt, _loadAt, _resumeAt;
    private Vector3 _progress;

    public bool Start(FarmProfile profile, bool[] caves, long now)
    {
        if (Running) { State = "请先停止当前农怪"; return false; }
        var c = host.Context;
        string? reason = !c.Ready || c.Loading || !c.LoggedIn || c.Dead || c.Busy || c.Combat ? "请先登录、脱战并等待加载完成" :
            c.Scope.Territory != 613 || c.Scope.World == 0 ? "请先到红玉海" :
            !host.NavigationReady || !host.MovementAllowed ? "请等待国服 vnavmesh 网格就绪并允许移动" :
            host.NavigationBusy ? "请先停止其他导航任务" :
            profile.Names.Length == 0 ? "请至少勾选一种小怪" :
            !c.Diving && !RubyCaves.InTamamizu(c.Position) && (c.Position.Y > -100 || !RubyCaves.All.Any(a=>Vector3.Distance(a.Interior,c.Position)<170)) ? "当前位置不在农怪洞穴或玉水村；请先到玉水村，或在红玉海下潜后开始" : null;
        _caves = Enumerable.Range(0,3).Where(i=>i<caves.Length && caves[i]).ToArray();
        if (_caves.Length == 0) reason = "请至少选择一个洞穴";
        if (reason != null) { State=reason; return false; }
        _scope=c.Scope; _profile=profile with { RequireAllTargets=false }; CompletedVisits=0;
        _moving=_wasLoading=_defending=false; _resumeAt=now; Progress.Reset(_profile);Completed=false;travel.Reset();Running=true;
        awaitingSceneReady=false;sceneReadySince=null;
        // Start in the current cave if selected; otherwise exit it before heading to the first selected cave.
        CaveIndex=Enumerable.Range(0,3).MinBy(i=>Vector3.DistanceSquared(RubyCaves.All[i].Interior,c.Position));
        if(!c.Diving && RubyCaves.InTamamizu(c.Position))
        {
            CaveIndex=_caves.MinBy(i=>Vector3.DistanceSquared(RubyCaves.All[i].Exterior,RubyCaves.Tamamizu.Exterior));
            SetPhase(RubyPhase.TownExitApproach,now);State="准备从玉水村出村潜水，随后开始农怪";return true;
        }
        SetPhase(c.Diving ? RubyPhase.Approach : (_caves.Contains(CaveIndex) ? RubyPhase.Scan : RubyPhase.ExitApproach),now);
        if(c.Diving) CaveIndex=_caves.MinBy(i=>Vector3.DistanceSquared(RubyCaves.All[i].Exterior,c.Position));
        State="准备三个洞穴循环"; return true;
    }

    public void Stop(string reason="已停止")
    {
        if (!Running) { State=reason; return; }
        Running=false;
        try { _farm.Stop(reason); }
        finally
        {
            try { if(_moving)host.StopMovement(); }
            finally { if(_defending)host.SetCombat(false); _defending=_moving=false; State=reason; }
        }
    }

    public void Tick(long now)
    {
        if (!Running) return;
        var c=host.Context;
        if (!c.LoggedIn && !c.Loading) {Stop("已登出，农怪已停止");return;}
        // Cave fades can briefly remove the player or expose zero IDs. Nonzero mismatches are always a stop.
        if ((c.Scope.World!=0 && c.Scope.World!=_scope.World) ||
            (c.Scope.Territory!=0 && c.Scope.Territory!=613) ||
            (c.Ready && !c.Loading && c.Scope.World!=0 && c.Scope.Territory==613 && c.Scope.Instance!=_scope.Instance))
        {Stop("服务器、地图或分线已变化，农怪已停止");return;}
        if (c.Loading || !c.Ready)
        {
            sceneReadySince=null;
            if (!_wasLoading) {_wasLoading=true;_loadAt=now;Progress.ForgetVisible();}
            if(now-_loadAt>120000) {Stop("洞穴加载超时，已停止");return;}
            State=$"{RubyCaves.All[CaveIndex].Name}：等待加载动画结束";return;
        }
        if(_wasLoading)
        {
            _wasLoading=false;
            _phaseAt+=now-_loadAt;_moveAt+=now-_loadAt;_progressAt=now;
            _resumeAt=now;awaitingSceneReady=true;sceneReadySince=null;
        }
        if(now<_resumeAt) {State="加载完成，等待场景稳定";return;}
        if(c.Dead){Stop("角色已死亡，农怪已停止");return;}
        if(c.Busy){sceneReadySince=null;State="等待洞穴过渡动画";if(now-_phaseAt>180000)Stop("过渡动画超时，已停止");return;}
        if(c.Scope!=_scope){sceneReadySince=null;State="等待场景身份恢复";if(now-_phaseAt>180000)Stop("场景身份未恢复，已停止");return;}
        if(!host.NavigationReady){sceneReadySince=null;State="等待洞穴导航网格就绪";if(now-_phaseAt>180000)Stop("网格加载超时，已停止");return;}
        if(!host.MovementAllowed){Stop("vnavmesh 移动已暂停，农怪已停止");return;}
        if(awaitingSceneReady)
        {
            if(!TransitionDestinationReady(c))
            {
                sceneReadySince=null;State="等待洞穴进出状态恢复";
                if(now-_phaseAt>180000)Stop("洞穴进出状态未恢复，已停止");
                return;
            }
            sceneReadySince??=now;
            if(now-sceneReadySince.Value<350){State="加载完成，等待场景稳定";return;}
            awaitingSceneReady=false;sceneReadySince=null;
        }
        var mobs=host.Mobs;
        if(mobs.Any(m=>m.NameId==5984 && m.Hp>0)){Stop("发现巨大鳐，已停止农怪；请通知狩猎玩家");return;}
        Progress.Observe(mobs);
        if(Progress.Completed){Completed=true;Stop("已勾选小怪的数量目标全部完成，农怪已停止");host.ClearTarget();return;}
        if(_defending && Progress.Done(defenseName))
        {ReleaseMovement();host.SetCombat(false);host.ClearTarget();_defending=false;_phaseAt+=now-_defendAt;}
        if(c.Combat && Phase!=RubyPhase.Farm)
        {
            var justDefending=!_defending;
            if(!_defending)
            {
                ReleaseMovement();
                if(c.Mounted || c.Flying){travel.Dismount(now);State=travel.State;if(travel.Failed)Stop(State);return;}
                var enemy=mobs.Where(m=>_profile.Names.Contains(m.NameId) && !Progress.Done(m.NameId) && m.Hp>0 &&
                    Vector3.Distance(m.Position,c.Position)<=40).OrderBy(m=>Vector3.DistanceSquared(m.Position,c.Position)).FirstOrDefault();
                if(enemy.Id==0)
                {
                    if(mobs.Any(m=>_profile.Names.Contains(m.NameId) && Progress.Done(m.NameId) && m.Hp>0 && Vector3.Distance(m.Position,c.Position)<=40))
                    {State="已达标种类不再主动攻击，等待脱战后继续";if(now-_phaseAt>180000)Stop("未能脱战，请手动处理后重新开始");return;}
                    Stop("遭遇非目标怪物，请手动处理战斗后重新开始");return;
                }
                host.Focus(enemy.Id);defenseName=enemy.NameId;host.SetCombat(true);_defending=true;_defendAt=now;
            }
            var engaged=mobs.Where(m=>_profile.Names.Contains(m.NameId) && !Progress.Done(m.NameId) && m.Hp>0 && FarmSession.Finite(m.Position) &&
                Vector3.Distance(m.Position,c.Position)<=40).OrderBy(m=>Vector3.DistanceSquared(m.Position,c.Position)).FirstOrDefault();
            if(engaged.Id!=0 && !justDefending){host.Focus(engaged.Id);defenseName=engaged.NameId;}
            if(engaged.Id!=0 && host.ManagesCombatMovement && Vector3.Distance(engaged.Position,c.Position)>host.AttackDistance(engaged))
            {Navigate(engaged.Position,false,now,false,"靠近交战中的观梦螺");}
            else ReleaseMovement();
            State="巡逻途中遭遇观梦螺，等待所选循环击杀后继续";
            if(now-_defendAt>180000)Stop("战斗持续过久，已停止");return;
        }
        if(_defending){ReleaseMovement();host.SetCombat(false);_defending=false;_phaseAt+=now-_defendAt;_resumeAt=now;}
        var cave=RubyCaves.All[CaveIndex];
        switch(Phase)
        {
            case RubyPhase.TownExitApproach:
                if(c.Diving){ReleaseMovement();SetPhase(RubyPhase.Approach,now);return;}
                if(Vector3.Distance(c.Position,RubyCaves.Tamamizu.Interior)<4){ReleaseMovement();SetPhase(RubyPhase.TownExit,now);return;}
                if(!PrepareTravel(now,false))return;
                Navigate(RubyCaves.Tamamizu.Interior,false,now,false,"玉水村：骑乘前往海底出口");
                break;
            case RubyPhase.TownExit:
                if(c.Diving && Vector3.Distance(c.Position,RubyCaves.Tamamizu.Exterior)<30)
                {ReleaseMovement();SetPhase(RubyPhase.Approach,now);return;}
                if(!PrepareTravel(now,false))return;
                Navigate(RubyCaves.CrossTrigger(RubyCaves.Tamamizu,false),c.Flying,now,true,"玉水村：保持骑乘出村并等待下潜过渡");
                break;
            case RubyPhase.Scan:
                if(c.Diving){Stop("尚未完成进入洞穴，请检查入口");return;}
                if(Vector3.Distance(c.Position,cave.ScanPoint)<4){ReleaseMovement();SetPhase(RubyPhase.Farm,now);return;}
                if(!PrepareTravel(now,FlyBetweenTargets))return;
                Navigate(cave.ScanPoint,MountBetweenTargets && FlyBetweenTargets,now,false,cave.Name+"：进入洞内扫描区");
                break;
            case RubyPhase.Farm:
                if(c.Diving){Stop("洞内巡怪时意外进入水中，请检查位置");return;}
                var candidates=mobs.Where(m=>_profile.Names.Contains(m.NameId) && !Progress.Done(m.NameId) && m.Hp>0 && FarmSession.Finite(m.Position) &&
                    Vector3.Distance(m.Position,cave.Interior)<200 && Math.Abs(m.Position.Y-c.Position.Y)<40 &&
                    Enumerable.Range(0,3).MinBy(i=>Vector3.DistanceSquared(m.Position,RubyCaves.All[i].ScanPoint))==CaveIndex).ToList();
                if(_farm.Running && !c.Combat && candidates.Count==0)
                {
                    _farm.Stop("该洞穴已无未完成目标，立即前往下一洞");FinishVisit(now);return;
                }
                if(!_farm.Running)
                {
                    var points=candidates.Select(m=>m.Position).Distinct().Take(80).ToList();
                    if(points.Count==0)
                    {
                        State=cave.Name+"：等待附近小怪加载或刷新";
                        if(now-_phaseAt>=2000) FinishVisit(now);
                        return;
                    }
                    var routeProfile=_profile with{PointNames=candidates.GroupBy(m=>m.Position).ToDictionary(g=>g.Key,g=>g.First().NameId)};
                    if(!_farm.Start(routeProfile,points,now)){Stop(_farm.State);return;}
                }
                _farm.Tick(now);State=cave.Name+"："+_farm.State;
                if(!_farm.Running){Stop(_farm.State);return;}
                if(_farm.RoundsCompleted>0){_farm.Stop("该洞穴本轮完成");FinishVisit(now);}
                break;
            case RubyPhase.ExitApproach:
                if(c.Diving){Stop("出口前已意外进入水中，请检查当前位置");return;}
                if(Vector3.Distance(c.Position,cave.Interior)<4){ReleaseMovement();SetPhase(RubyPhase.Exit,now);return;}
                if(!PrepareTravel(now,FlyBetweenTargets))return;
                Navigate(cave.Interior,MountBetweenTargets && FlyBetweenTargets,now,false,cave.Name+"：返回洞内出口前");
                break;
            case RubyPhase.Exit:
                if(c.Diving && Vector3.Distance(c.Position,cave.Exterior)<30)
                {ReleaseMovement();CaveIndex=NextCave();SetPhase(RubyPhase.Approach,now);return;}
                if(!PrepareTravel(now,FlyBetweenTargets))return;
                Navigate(RubyCaves.CrossTrigger(cave,false),c.Flying || (MountBetweenTargets && FlyBetweenTargets),now,true,cave.Name+"：保持骑乘穿过出口");
                break;
            case RubyPhase.Approach:
                if(!c.Diving){Stop("洞间移动需要已下潜状态；请检查洞穴出口");return;}
                if(!PrepareTravel(now,true))return;
                if(Vector3.Distance(c.Position,cave.Exterior)<4){ReleaseMovement();SetPhase(RubyPhase.Enter,now);return;}
                Navigate(cave.Exterior,true,now,false,"潜水前往"+cave.Name);
                break;
            case RubyPhase.Enter:
                if(!c.Diving && Vector3.Distance(c.Position,cave.Interior)<30)
                {ReleaseMovement();SetPhase(RubyPhase.Scan,now);return;}
                if(!PrepareTravel(now,true))return;
                Navigate(RubyCaves.CrossTrigger(cave,true),true,now,true,cave.Name+"：穿过入口并等待过渡");
                break;
        }
    }
    private int NextCave() {var at=Array.IndexOf(_caves,CaveIndex);return _caves[(at+1)%_caves.Length];}
    private bool TransitionDestinationReady(FarmContext c)=>Phase switch
    {
        RubyPhase.Enter=>!c.Diving && Vector3.Distance(c.Position,RubyCaves.All[CaveIndex].Interior)<30,
        RubyPhase.Exit=>c.Diving && Vector3.Distance(c.Position,RubyCaves.All[CaveIndex].Exterior)<30,
        RubyPhase.TownExit=>c.Diving && Vector3.Distance(c.Position,RubyCaves.Tamamizu.Exterior)<30,
        _=>true
    };
    private void FinishVisit(long now)
    {
        CompletedVisits++;
        if(_caves.Length==1 && _caves.Contains(CaveIndex)){SetPhase(RubyPhase.Farm,now);_resumeAt=now+10000;}
        else SetPhase(RubyPhase.ExitApproach,now);
    }
    private void SetPhase(RubyPhase phase,long now){Phase=phase;_phaseAt=_moveAt=_progressAt=now;_moving=false;travel.Reset();}
    private bool PrepareTravel(long now,bool fly)
    {
        if(!MountBetweenTargets || _moving)return true;
        if(travel.Prepare(now,fly))return true;
        State=travel.State;if(travel.Failed)Stop(State);return false;
    }
    private void ReleaseMovement(){if(_moving)host.StopMovement();_moving=false;}
    private void Navigate(Vector3 destination,bool fly,long now,bool transition,string status)
    {
        State=status;var c=host.Context;
        if(now-_phaseAt>180000){Stop("洞穴路段或进出过渡超时，请检查入口与网格");return;}
        if(!_moving)
        {
            if(host.NavigationBusy){Stop("检测到其他导航任务，已停止");return;}
            if(!(transition ? host.MoveDirect(destination,fly) : host.Move(destination,fly)))
            {Stop("无法提交洞穴路线，请检查 vnavmesh");return;}
            _moving=true;_moveAt=_progressAt=now;_progress=c.Position;return;
        }
        if(Vector3.Distance(c.Position,_progress)>1){_progress=c.Position;_progressAt=now;}
        if(now-_moveAt>1500 && !host.NavigationBusy)
        {
            // At a portal the executor may finish just before its fade. Wait, never repeatedly send the route.
            if(transition && Vector3.Distance(c.Position,destination)<4)
            {if(now-_progressAt>20000)Stop("已到洞口但未完成进出过渡，请检查位置");return;}
            Stop("寻路已被停止或无法到达洞口；不会自动重启");return;
        }
        if(now-_progressAt>30000)Stop("洞穴路段移动卡住，已停止");
    }
}
