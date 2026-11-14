// LuckyStarCN changes, 2026-11-13. AGPL-3.0-or-later.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace LuckyStar;

public readonly record struct FarmScope(uint World, uint Territory, uint Instance);
public readonly record struct FarmMob(ulong Id, uint NameId, Vector3 Position, uint Hp, uint MaxHp, float HitboxRadius = 0);
public sealed record FarmProfile(string Name, uint Territory, uint[] Names, bool Ground, uint StopOnName = 0, bool RequireAllTargets = true)
{
    public IReadOnlyDictionary<uint,int>? Goals { get; init; }
    public IReadOnlyDictionary<Vector3,uint>? PointNames { get; init; }
}
public readonly record struct FarmContext(bool Ready, FarmScope Scope, Vector3 Position,
    bool Dead, bool Busy, bool Combat, bool Mounted, bool Flying, bool Diving,
    bool Loading = false, bool LoggedIn = true, bool Casting = false);

public interface IFarmHost
{
    FarmContext Context { get; }
    IReadOnlyList<FarmMob> Mobs { get; }
    bool NavigationReady { get; }
    bool NavigationBusy { get; }
    bool MovementAllowed { get; }
    float EngageDistance => 25;
    bool ManagesCombatMovement => EngageDistance < 25;
    float AttackDistance(FarmMob target) => EngageDistance + (float.IsFinite(target.HitboxRadius) ? Math.Max(0,target.HitboxRadius) : 0);
    bool Move(Vector3 position, bool fly);
    bool MoveDirect(Vector3 position, bool fly);
    void StopMovement();
    void SetCombat(bool enabled);
    void Focus(ulong id);
    void ClearTarget() { }
    void Mount();
    void TakeOff();
    void Dismount();
    Vector3? GroundPoint(Vector3 position) => position;
}

/// <summary>One session belongs to one world, territory and instance. No server progress is inferred.</summary>
public sealed class FarmSession(IFarmHost host, FarmProgress? sharedProgress = null)
{
    public bool Running { get; private set; }
    public string State { get; private set; } = "未运行";
    public FarmProfile? Profile { get; private set; }
    public FarmScope BoundScope=>_scope;
    public IReadOnlyList<Vector3> Points => _points;
    public int Index { get; private set; }
    public int RoundsCompleted { get; private set; }
    public int RoutesSubmitted { get; private set; }
    public object NavigationDiagnostics=>new{Moving=_moving,FlightApproach=flightApproach,OnFootApproach=onFootApproach,
        Target=_target,Goal=new{_moveGoal.X,_moveGoal.Y,_moveGoal.Z},RoutesSubmitted};
    public FarmProgress Progress { get; } = sharedProgress ?? new();
    public Dictionary<uint, int> Deaths => Progress.Deaths;
    public bool Completed { get; private set; }
    public bool MountBetweenTargets { get; set; } = true;
    public bool FlyBetweenTargets { get; set; } = true;
    private readonly FarmTravel travel = new(host);
    private bool onFootApproach;
    private bool flightApproach;
    private bool firstTargetDelayPending;
    public int DelaySeconds { get; set; }
    private List<Vector3> _points = [];
    private FarmScope _scope;
    private bool _moving, _combatOn, _chasing, _approachingTarget;
    private long _moveAt, _progressAt, _waitUntil, _targetSince;
    private Vector3 _lastProgress, _moveGoal;
    private ulong _target;
    private uint targetName;

    public bool Start(FarmProfile profile, IEnumerable<Vector3> points, long now)
    {
        if (Running) return Reject("请先停止当前农怪");
        var c = host.Context;
        if (!c.Ready || c.Dead || c.Busy || c.Combat || c.Scope.World == 0) return Reject("请先登录、脱战并等待地图加载完成");
        if (c.Scope.Territory != profile.Territory) return Reject("请先前往对应地图：" + profile.Name);
        if (profile.Names.Length == 0) return Reject("请至少选择一种小怪");
        if (profile.Ground && c.Diving) return Reject("请先进入海底洞穴再开始洞内农怪");
        if (!host.NavigationReady || !host.MovementAllowed) return Reject("请安装并启用 vnavmesh，等待网格就绪并允许移动");
        if (host.NavigationBusy) return Reject("vnavmesh 正在移动，请先停止其他路线");
        var valid = points.Where(Finite).Distinct().ToList();
        if (valid.Count == 0) return Reject("没有可用点位；红玉海请站在有两类观梦螺的洞穴陆地上");
        if (profile.Ground && profile.RequireAllTargets && profile.Names.Any(n => !host.Mobs.Any(m => m.NameId == n && m.Hp > 0 &&
            Vector3.Distance(m.Position, c.Position) <= 120 && Math.Abs(m.Position.Y - c.Position.Y) <= 18)))
            return Reject("附近未找到每种已勾选的小怪；请靠近两类观梦螺，或调整勾选后重试");
        Profile = profile; _scope = c.Scope; _points = Order(valid, c.Position);
        Index = 0; RoundsCompleted = RoutesSubmitted = 0; _moving = _chasing = _approachingTarget = false; _target = 0; _combatOn = false;
        _waitUntil = now; _targetSince = now;
        if(sharedProgress==null)Progress.Reset(profile);
        Completed=false;targetName=0;onFootApproach=flightApproach=false;firstTargetDelayPending=true;travel.Reset();Running = true; State = "准备农怪";
        return true;
    }

    private bool Reject(string reason) { State = reason; return false; }

    public void Stop(string reason = "已停止")
    {
        if (Running)
        {
            Running = false;
            try { if (_moving) host.StopMovement(); }
            finally { if (_combatOn) host.SetCombat(false); }
        }
        _moving = _combatOn = false; _target = 0; State = reason;
    }

    public void Tick(long now)
    {
        if (!Running || Profile is not { } profile) return;
        var c = host.Context;
        if (!c.Ready || c.Scope != _scope) { Stop("地图、服务器或分线已变化，农怪已停止"); return; }
        if (c.Dead) { Stop("角色已死亡，农怪已停止"); return; }
        if (!host.NavigationReady || !host.MovementAllowed) { Stop("导航不可用或移动已暂停，农怪已停止"); return; }
        var mobs = host.Mobs;
        if (profile.StopOnName != 0 && mobs.Any(m => m.NameId == profile.StopOnName && m.Hp > 0))
        { Stop("发现巨大鳐，已停止农怪；请通知狩猎玩家"); return; }
        Progress.Observe(mobs);
        if(Progress.Completed){Completed=true;Stop("已勾选小怪的数量目标全部完成，农怪已停止");host.ClearTarget();return;}
        if(_target!=0 && Progress.Done(targetName))
        {Combat(false);host.ClearTarget();_target=0;targetName=0;onFootApproach=false;}
        if (c.Busy) { Stop("角色正在切图、过场或执行其他操作，农怪已停止"); return; }
        if (profile.Ground && c.Diving) { Stop("已离开洞穴陆地，农怪已停止"); return; }
        if (c.Combat)
        {
            firstTargetDelayPending=false;
            if(c.Mounted || c.Flying){if(_moving){host.StopMovement();_moving=false;}WaitDismount(now);return;}
            if (_moving && !_chasing) { host.StopMovement(); _moving = false; }
            var enemy = mobs.Where(m => profile.Names.Contains(m.NameId) && !Progress.Done(m.NameId) && m.Hp > 0 && Finite(m.Position) &&
                Vector3.Distance(m.Position,c.Position) <= 40).OrderBy(m => m.Id==_target ? -1 : Vector3.DistanceSquared(m.Position,c.Position)).FirstOrDefault();
            var justEnabled=!_combatOn;
            if (!_combatOn)
            {
                if (enemy.Id == 0)
                {
                    if(mobs.Any(m=>profile.Names.Contains(m.NameId) && Progress.Done(m.NameId) && m.Hp>0 && Vector3.Distance(m.Position,c.Position)<=40))
                    {State="已达标种类不再主动攻击，等待脱战后寻找未完成种类";if(now-_targetSince>180000)Stop("未能脱战，请手动处理后重新开始");return;}
                    Stop("遭遇非目标怪物，请手动处理战斗后重新开始"); return;
                }
                _target=enemy.Id;targetName=enemy.NameId; _targetSince=now; host.Focus(enemy.Id); Combat(true);
            }
            if(enemy.Id!=0)
            {
                if(_target!=enemy.Id){_target=enemy.Id;targetName=enemy.NameId;_targetSince=now;}
                if(!justEnabled)host.Focus(enemy.Id);
            }
            if(enemy.Id!=0 && host.ManagesCombatMovement && Vector3.Distance(enemy.Position,c.Position)>host.AttackDistance(enemy))
            {
                if(!_moving)
                {
                    if(host.NavigationBusy || !host.Move(enemy.Position,false)){Stop("无法靠近交战目标，请检查地面寻路");return;}
                    _moving=_chasing=_approachingTarget=true;_moveGoal=enemy.Position;_moveAt=_progressAt=now;_lastProgress=c.Position;
                }
                else
                {
                    if(Vector3.Distance(c.Position,_lastProgress)>1){_lastProgress=c.Position;_progressAt=now;}
                    if(now-_progressAt>20000 || now-_moveAt>60000 || (now-_moveAt>1500 && !host.NavigationBusy))
                    {Stop("靠近交战目标的路线已中断，农怪已停止");return;}
                }
                State="靠近交战目标";return;
            }
            if(_chasing){host.StopMovement();_moving=_chasing=false;}
            if (now - _targetSince > 180000) { Stop("战斗持续过久，请检查自动循环和角色状态"); return; }
            State = "战斗中，等待脱战"; return;
        }
        if(_chasing){host.StopMovement();_moving=_chasing=false;}
        if (now < _waitUntil) { State = "等待下一轮刷新"; return; }
        var point = _points[Index];
        var target = mobs.Where(m => profile.Names.Contains(m.NameId) && !Progress.Done(m.NameId) && m.Hp>0 && Finite(m.Position) &&
            (profile.Ground
                ? Vector3.Distance(m.Position,c.Position)<=120 && Math.Abs(m.Position.Y-c.Position.Y)<=18 && _points.Any(p=>Vector3.Distance(m.Position,p)<=25)
                : Vector3.Distance(m.Position,point)<=25))
            .OrderBy(m=>m.Id==_target ? -1 : Vector3.DistanceSquared(m.Position,c.Position)).FirstOrDefault();
        if(target.Id==0 && profile.PointNames?.TryGetValue(point,out var pointName)==true && Progress.Done(pointName))
        {
            ReleaseMovement();Combat(false);Advance(now);State="跳过已完成种类的点位";return;
        }
        if (_moving)
        {
            // Stop the owned route before dismounting, using the live target position.
            var tracked=mobs.FirstOrDefault(m=>m.Id==_target && m.Hp>0 && !Progress.Done(m.NameId));
            if(_approachingTarget && tracked.Id==0){ReleaseMovement();onFootApproach=false;_approachingTarget=false;}
            var nearTarget=target.Id!=0 && Vector3.Distance(c.Position,target.Position)<=
                (c.Mounted ? DismountDistance(target) : host.AttackDistance(target)) &&
                !c.Flying;
            var atGoal=flightApproach && c.Flying ? AtLandingPoint(c.Position) :
                Vector3.Distance(c.Position,_moveGoal)<=Math.Min(3,host.EngageDistance);
            if(_moving && (nearTarget || atGoal))
            {ReleaseMovement();if(flightApproach)onFootApproach=true;}
            if(_moving)
            {
                if (Vector3.Distance(c.Position, _lastProgress)>=1){_lastProgress=c.Position;_progressAt=now;}
                if(now-_moveAt>120000 || now-_progressAt>20000){Stop("移动超时或卡住，请检查路线后重新开始");return;}
                if(now-_moveAt>1500 && !host.NavigationBusy){Stop("寻路已被停止或未找到路线；不会自动重启");return;}
                State=$"骑乘巡怪／前往点位 {Index+1}/{_points.Count}";return;
            }
        }
        if(target.Id!=0)
        {
            if(_target!=target.Id){Combat(false);_target=target.Id;targetName=target.NameId;_targetSince=now;onFootApproach=flightApproach=false;travel.Reset();}
            if(now-_targetSince>120000){Stop("小怪长时间未击杀，请检查自动循环配置");return;}
            if(firstTargetDelayPending && now-_targetSince<Math.Clamp(DelaySeconds,0,120)*1000L){State="首只小怪延迟等待";return;}
            firstTargetDelayPending=false;
            var distance=Vector3.Distance(c.Position,target.Position);
            if(distance>DismountDistance(target) && !onFootApproach)
            {
                Combat(false);
                if(MountBetweenTargets && !PrepareTravel(now))return;
                MoveTo(target.Position,MountBetweenTargets && FlyBetweenTargets,now,true);return;
            }
            // Arrival hands control to dismounting. The real landing animation can descend
            // below the mesh floor while Flying remains true; keep waiting for its flags.
            // Before that handoff, never cancel a flight high above the ground.
            if(c.Flying && !onFootApproach && !AtLandingPoint(c.Position))
            {
                Combat(false);
                if(flightApproach){Stop("飞行路线已结束但尚未到达落地点，请检查地面与导航");return;}
                MoveTo(target.Position,true,now,true);return;
            }
            if(c.Mounted || c.Flying){Combat(false);onFootApproach=true;WaitDismount(now);return;}
            if(distance>host.AttackDistance(target))
            {
                Combat(false);onFootApproach=true;MoveTo(target.Position,false,now,true);return;
            }
            host.Focus(target.Id);Combat(true);State="等待所选循环击杀小怪";return;
        }
        Combat(false);_target=0;onFootApproach=false;_approachingTarget=false;
        if(Vector3.Distance(c.Position,point)>4)
        {
            if(MountBetweenTargets && !PrepareTravel(now))return;
            MoveTo(point,MountBetweenTargets && FlyBetweenTargets,now,false);return;
        }
        Advance(now);State="前往下一点";
    }

    private float DismountDistance(FarmMob target)=>host.AttackDistance(target)+3;
    private bool AtLandingPoint(Vector3 position)
    {
        if(!flightApproach)return false;
        var delta=position-_moveGoal;
        if(delta.Length()<=1 && Math.Abs(delta.Y)<=0.35f)return true;
        // vnavmesh can finish a flight just above its queried mesh floor. At the
        // same horizontal point, a short native dismount can safely land there.
        // Keep active flights and higher/sideways cancellations under the strict rule.
        return !host.NavigationBusy && delta.X*delta.X+delta.Z*delta.Z<=0.25f && delta.Y is >=-0.5f and <=1.5f;
    }
    private void ReleaseMovement(){if(_moving)host.StopMovement();_moving=false;}
    private bool PrepareTravel(long now)
    {
        if(travel.Prepare(now,FlyBetweenTargets))return true;
        State=travel.State;if(travel.Failed)Stop(State);return false;
    }
    private void WaitDismount(long now)
    {
        travel.Dismount(now);State=travel.State;if(travel.Failed)Stop(State);
    }
    private void MoveTo(Vector3 position,bool fly,long now,bool target)
    {
        if(host.NavigationBusy){Stop("检测到其他导航任务，农怪已停止");return;}
        if(fly && target)
        {
            var away=host.Context.Position-position;away.Y=0;
            away=away.LengthSquared()>0.01f?Vector3.Normalize(away):Vector3.UnitX;
            var candidate=position+away*Math.Clamp(host.EngageDistance*0.8f,1.5f,3);
            var ground=host.GroundPoint(candidate);
            if(ground is not { } point || !Finite(point)){Stop("目标旁没有可用落地点，农怪已停止");return;}
            position=point;
        }
        if(!host.Move(position,fly)){Stop("无法提交路线，请检查 vnavmesh");return;}
        RoutesSubmitted++;
        flightApproach=fly && target;
        _moving=true;_approachingTarget=target;_moveGoal=position;_moveAt=_progressAt=now;_lastProgress=host.Context.Position;
    }
    private void Advance(long now)
    {
        Index++;
        if(Index>=_points.Count){Index=0;RoundsCompleted++;firstTargetDelayPending=true;_waitUntil=now+10000;}
        else _waitUntil=now;
    }

    private void Combat(bool on) { if (on != _combatOn) { host.SetCombat(on); _combatOn = on; } }
    public static bool Finite(Vector3 p) => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z);
    public static List<Vector3> Order(IEnumerable<Vector3> points, Vector3 from)
    {
        var left = points.ToList(); List<Vector3> result = [];
        while (left.Count > 0) { var next = left.OrderBy(p => Vector3.DistanceSquared(from, p)).First(); result.Add(next); left.Remove(next); from = next; }
        return result;
    }
}
