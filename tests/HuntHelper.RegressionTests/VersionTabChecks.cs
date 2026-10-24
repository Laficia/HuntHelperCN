using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using System.Numerics;

internal static class VersionTabChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("version tabs sort A ranks newest first and preserve other records", Groups),
        ("each server remembers its own selected version", Worlds),
        ("empty version tabs disappear and selection falls back", Empty),
        ("filtered rows retain their original indices and record identity", Indices),
        ("next hunt and selection stay within the visible version", Commands),
        ("removal and revival affect only visible records in the selected server", Mutations),
        ("deleting a version in the current server pauses recording", Pause),
        ("bulk revival reports affected records and preserves other versions and servers", RestoreCount)
    ];
    private static HuntTrainMob Mob(uint id, uint world = 101) => new($"Monster {id}",id,134,30,1,"Map",
        new Vector2(20,20),DateTime.UtcNow,false,world,$"World {world}","",new Vector3(10,0,10));
    private static TrainVersionFilter Filter() => new(m => m.MobID / 100 is >= 2 and <= 7 ? (int)m.MobID / 100 : null);
    private static void Check(bool value) { if (!value) throw new InvalidOperationException("Version-tab assertion failed."); }

    private static void Groups()
    {
        var rows = new[] {Mob(401),Mob(702),Mob(501),Mob(601),Mob(701),Mob(101),Mob(301),Mob(201)};
        var groups=Filter().Groups(rows);
        Check(groups.Select(g=>g.Label).SequenceEqual(new[]{"7A","6A","5A","4A","3A","2A","其它","全部"}));
        Check(groups[0].Count==2 && groups[^1].Count==8 && groups[^2].Count==1);
    }
    private static void Worlds()
    {
        var f=Filter();var rows=new[]{Mob(701),Mob(601)};
        Check(f.Selected(101,rows)==7);f.Select(101,6);Check(f.Selected(102,rows)==7);Check(f.Selected(101,rows)==6);
        f.Select(102,null);Check(f.VisibleIndices(102,rows).Count==2 && f.Selected(101,rows)==6);
    }
    private static void Empty()
    {
        var f=Filter();var rows=new List<HuntTrainMob>{Mob(701),Mob(601)};
        Check(f.Selected(101,rows)==7);rows.RemoveAt(0);Check(f.Groups(rows).All(g=>g.Version!=7));Check(f.Selected(101,rows)==6);
        rows.Clear();Check(f.Groups(rows).Count==0 && f.VisibleIndices(101,rows).Count==0);
        rows.Add(Mob(401));Check(f.Selected(101,rows)==4);
    }
    private static void Indices()
    {
        var f=Filter();var rows=new List<HuntTrainMob>{Mob(601),Mob(701),Mob(501),Mob(702)};
        Check(f.VisibleIndices(101,rows).SequenceEqual(new[]{1,3}));
        var first=rows[1];var hidden=rows[2];(rows[1],rows[3])=(rows[3],rows[1]);
        Check(ReferenceEquals(rows[3],first) && ReferenceEquals(rows[2],hidden));
        f.Select(101,6);Check(f.VisibleIndices(101,rows).SequenceEqual(new[]{0}));
    }
    private static void Commands()
    {
        var f=Filter();var rows=new[]{Mob(701),Mob(601),Mob(702),Mob(703)};
        rows[0].Dead=true;rows[2].Dead=true;var visible=f.VisibleIndices(101,rows);
        Check(TrainVersionFilter.Clamp(1,visible)==0);Check(TrainVersionFilter.NextLiving(0,visible,rows)==3);
        rows[3].Dead=true;Check(TrainVersionFilter.NextLiving(0,visible,rows)==-1 && !rows[1].Dead);
        Check(TrainVersionFilter.Clamp(3,Array.Empty<int>())==0);
    }
    private static void Mutations()
    {
        using var directory=new TemporaryDirectory();var world=new HuntTrainWorld(101,"A","",0);
        using var manager=new TrainManager(null!,null!,Path.Combine(directory.Path,"train.json"),()=>world);
        manager.AddMob(Mob(701));manager.AddMob(Mob(601));manager.AddMob(Mob(702));
        manager.SetDead(1,true);manager.SetDead(2,true);
        world=new(102,"B","",0);manager.AddMob(Mob(701));manager.SetDead(0,true);
        var b=manager.HuntTrain.Single();manager.SelectWorld(101);
        var filter=Filter();var visible=filter.VisibleIndices(101,manager.HuntTrain).Select(i=>manager.HuntTrain[i]).ToList();
        manager.RemoveRecords(visible.Where(m=>m.Dead));Check(manager.HuntTrain.Count==2 && manager.HuntTrain[1].MobID==601);
        manager.SetDead(0,true);manager.RestoreRecords(new[]{manager.HuntTrain[0],b});
        Check(!manager.HuntTrain[0].Dead && manager.HuntTrain[1].Dead && b.Dead);
        manager.RemoveRecords(manager.HuntTrain.Where(m=>m.MobID==701),pauseRecording:true);
        Check(manager.HuntTrain.Single().MobID==601 && manager.GetTrainRecords(102).Count==1);
    }
    private static void Pause()
    {
        using var directory=new TemporaryDirectory();
        using var manager=new TrainManager(null!,null!,Path.Combine(directory.Path,"train.json"),()=>new HuntTrainWorld(101,"A","",0));
        manager.AddMob(Mob(701));manager.AddMob(Mob(601));manager.RecordTrain=true;
        manager.RemoveRecords(new[]{manager.HuntTrain[0]},pauseRecording:true);
        Check(!manager.RecordTrain && manager.HuntTrain.Single().MobID==601);
    }
    private static void RestoreCount()
    {
        using var directory=new TemporaryDirectory();
        using var manager=new TrainManager(null!,null!,Path.Combine(directory.Path,"train.json"),()=>new HuntTrainWorld(101,"A","",0));
        manager.AddMob(Mob(701));manager.AddMob(Mob(702));manager.AddMob(Mob(601));
        manager.SetDead(0,true);manager.SetDead(2,true);
        var other=Mob(701,102);other.Dead=true;manager.AddMob(other);manager.SelectWorld(101);
        var notifications=0;manager.ManualDeadStateChanged+=_=>notifications++;
        var restored=manager.RestoreRecords(new[]{manager.HuntTrain[0],manager.HuntTrain[1],other});
        Check(restored==1 && notifications==1 && !manager.HuntTrain[0].Dead && manager.HuntTrain[2].Dead && other.Dead);
        Check(manager.RestoreRecords(new[]{manager.HuntTrain[0]})==0 && notifications==1);
        using var loaded=new TrainManager(null!,null!,Path.Combine(directory.Path,"train.json"),()=>new HuntTrainWorld(101,"A","",0));
        Check(!loaded.HuntTrain.First(m=>m.MobID==701).Dead && loaded.HuntTrain.First(m=>m.MobID==601).Dead);
    }
}
