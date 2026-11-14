using System.Collections.Generic;
using System.Linq;

namespace LuckyStar;

/// <summary>Observed live-to-dead transitions in this run, shared across all cave visits.</summary>
public sealed class FarmProgress
{
    public Dictionary<uint,int> Deaths { get; } = [];
    private readonly Dictionary<ulong,FarmMob> observed = [];
    private readonly HashSet<uint> announced = [];
    private readonly Queue<uint> completedKinds = [];
    private FarmProfile? profile;
    public bool Completed => profile is { Goals: not null } p && p.Names.Length>0 && p.Names.All(Done);
    public bool Done(uint name) => profile?.Goals?.TryGetValue(name,out var goal)==true && goal>0 && Deaths.GetValueOrDefault(name)>=goal;
    public void Reset(FarmProfile value)
    {
        profile=value;Deaths.Clear();observed.Clear();announced.Clear();completedKinds.Clear();
    }
    public void ForgetVisible()=>observed.Clear();
    public void RestoreDeaths(IReadOnlyDictionary<uint,int> counts)
    {
        if(profile==null || counts.Any(p=>!profile.Names.Contains(p.Key) || p.Value is <0 or >100000))
            throw new System.ArgumentException("进度与当前农怪种类不匹配");
        Deaths.Clear();observed.Clear();announced.Clear();completedKinds.Clear();
        foreach(var pair in counts){Deaths[pair.Key]=pair.Value;if(Done(pair.Key))announced.Add(pair.Key);}
    }
    public uint[] TakeCompletedKinds()
    {
        var result=completedKinds.ToArray();completedKinds.Clear();return result;
    }
    public void Observe(IReadOnlyList<FarmMob> mobs)
    {
        if(profile==null)return;
        var present=new HashSet<ulong>();
        foreach(var mob in mobs.Where(m=>profile.Names.Contains(m.NameId) && m.Id!=0 && m.MaxHp>0))
        {
            present.Add(mob.Id);
            if(observed.TryGetValue(mob.Id,out var previous) && previous.NameId==mob.NameId && previous.Hp>0 && mob.Hp==0)
            {
                Deaths[mob.NameId]=Deaths.GetValueOrDefault(mob.NameId)+1;
                if(Done(mob.NameId) && announced.Add(mob.NameId))completedKinds.Enqueue(mob.NameId);
            }
            observed[mob.Id]=mob;
        }
        foreach(var id in observed.Keys.Where(id=>!present.Contains(id)).ToArray())observed.Remove(id);
    }
}
