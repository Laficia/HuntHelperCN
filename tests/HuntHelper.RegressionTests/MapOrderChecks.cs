using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json.Linq;

internal static class MapOrderChecks
{
    private static readonly uint[][] Orders =
    [ [612,620,621,613,614,622], [813,814,815,816,817,818], [957,956,958,959,961,960], [1187,1188,1189,1190,1191,1192] ];
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("4A discoveries follow Fringes Peaks Lochs Ruby Yanxia Steppe", () => DefaultOrder(0)),
        ("5A discoveries follow Lakeland Kholusia Amh IlMheg Raktika Tempest", () => DefaultOrder(1)),
        ("6A discoveries follow Thavnair Labyrinthos Garlemald Mare Elpis Ultima", () => DefaultOrder(2)),
        ("7A discoveries follow Urqopacha Kozamaka Yaktel Shaaloani Heritage Living", () => DefaultOrder(3)),
        ("default map territory identities match the shipped spawn-point dataset", MapIdentities),
        ("new discoveries preserve manual order across scans and restart", ManualOrder),
        ("Sonar insertion uses its explicit source server while local scans use visited server", WorldIsolation),
        ("same-map monsters and instances retain stable discovery order without deduplication loss", StableInstances),
        ("import and legacy saved sequences remain authoritative and S order stays unchanged", ImportAndS),
        ("insertion preserves selected record indices and never creates a manual recovery snapshot", SelectionAndBackups),
        ("unknown territories and other versions keep their relative order", OtherVersions),
    ];
    private static readonly HuntTrainWorld A=new(101,"A服","",0), B=new(102,"B服","",0);
    private static void Check(bool ok){if(!ok)throw new Exception("Default map-order assertion failed");}
    private static HuntTrainMob Mob(uint territory,uint id=0,uint instance=0,uint world=101) =>
        new("测试",id==0?territory:id,territory,30,instance,"地图",new(20,20),DateTime.UtcNow,false,world,world==101?"A服":"B服");
    private sealed class Host:IChatGui,IGameGui
    {public void Print(SeString _) => throw new Exception("Unexpected chat");public void OpenMapWithMapLink(MapLinkPayload _) => throw new Exception("Unexpected map");}
    private sealed record Npc(uint NameId,Vector3 Position):IBattleNpc{public SeString Name=>new(){TextValue="测试"};}
    private sealed class Fixture:IDisposable
    {
        public TemporaryDirectory Dir {get;}=new();
        public HuntTrainWorld World=A;
        public string PathName=>Path.Combine(Dir.Path,"HuntTrain.json");
        public TrainManager M {get;}
        public Fixture(){M=new(new Host(),new Host(),PathName,()=>World,defaultARankMapOrder:true);}
        public void Dispose(){M.Dispose();Dir.Dispose();}
    }
    private static void DefaultOrder(int n)
    {
        using var f=new Fixture();var order=Orders[n];
        foreach(var map in order.Reverse())f.M.AddMob(Mob(map));
        Check(f.M.HuntTrain.Select(m=>m.TerritoryID).SequenceEqual(order));
        // A second randomized pass, not just reverse input, must yield the same default.
        f.World=B;foreach(var i in new[]{2,5,0,4,1,3})f.M.AddMob(Mob(order[i]));
        Check(f.M.GetTrainRecords(102).Select(m=>m.TerritoryID).SequenceEqual(order));
        Check(f.M.Snapshots.List().Count==0);
    }
    private static void MapIdentities()
    {
        var data=JArray.Parse(File.ReadAllText(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../HuntHelper/Data/SpawnPointData.json"))));
        string[][] names = [ ["The Fringes","The Peaks","The Lochs","The Ruby Sea","Yanxia","The Azim Steppe"],
            ["Lakeland","Kholusia","Amh Araeng","Il Mheg","The Rak'tika Greatwood","The Tempest"],
            ["Thavnair","Labyrinthos","Garlemald","Mare Lamentorum","Elpis","Ultima Thule"],
            ["Urqopacha","Kozama'uka","Yak T'el","Shaaloani","Heritage Found","Living Memory"] ];
        for(var v=0;v<4;v++)for(var i=0;i<6;i++)Check(data.Single(r=>(uint)r["MapID"]! == Orders[v][i])["MapName"]!.Value<string>()==names[v][i]);
    }
    private static void ManualOrder()
    {
        using var f=new Fixture();foreach(var map in new uint[]{612,620,621})f.M.AddMob(Mob(map));
        var rows=f.M.HuntTrain.ToArray();Check(f.M.ReorderTrainMobs(rows,new[]{rows[2],rows[0],rows[1]}));
        var custom=f.M.HuntTrain.Select(m=>m.MobID).ToArray();f.M.RecordTrain=true;
        f.M.ObserveMob(new Npc(612,new(10,5,20)),true,612,30,0,"地图",100);
        f.M.RecordRelay(Mob(620));Check(f.M.HuntTrain.Select(m=>m.MobID).SequenceEqual(custom));
        using(var reload=new TrainManager(new Host(),new Host(),f.PathName,()=>A,defaultARankMapOrder:true))
        {
            Check(reload.HuntTrain.Select(m=>m.MobID).SequenceEqual(custom));reload.AddMob(Mob(613));
            Check(reload.HuntTrain.Where(m=>m.MobID!=613).Select(m=>m.MobID).SequenceEqual(custom));
        }
        Check(f.M.Snapshots.List().Count==1);
    }
    private static void WorldIsolation()
    {
        using var f=new Fixture();f.M.RecordTrain=true;f.M.SelectWorld(102);
        f.M.RecordRelay(Mob(622,world:102));f.M.RecordRelay(Mob(612,world:102));
        f.M.ObserveMob(new Npc(621,new(10,5,20)),true,621,30,0,"地图",100);
        f.M.ObserveMob(new Npc(620,new(10,5,20)),true,620,30,0,"地图",100);
        Check(f.M.GetTrainRecords(101).Select(m=>m.TerritoryID).SequenceEqual(new uint[]{620,621}));
        Check(f.M.GetTrainRecords(102).Select(m=>m.TerritoryID).SequenceEqual(new uint[]{612,622}));
        Check(f.M.SelectedWorldId==102 && f.M.GetTrainRecords(102).All(m=>m.WorldID==102));
    }
    private static void StableInstances()
    {
        using var f=new Fixture();f.M.AddMob(Mob(621));
        foreach(var (id,line) in new(uint,uint)[]{(1,2),(2,1),(1,1),(2,2)})f.M.AddMob(Mob(620,id,line));
        Check(f.M.HuntTrain.Take(4).Select(m=>(m.MobID,m.Instance)).SequenceEqual(new(uint,uint)[]{(1,2),(2,1),(1,1),(2,2)}));
        f.M.AddMob(Mob(620,1,2));Check(f.M.HuntTrain.Count==5 && f.M.HuntTrain[4].TerritoryID==621);
    }
    private static void ImportAndS()
    {
        using var f=new Fixture();f.M.Import(new[]{Mob(622),Mob(612)});Check(f.M.ImportTrainAll());
        Check(f.M.HuntTrain.Select(m=>m.TerritoryID).SequenceEqual(new uint[]{622,612}));
        f.M.RecordTrain=true;f.M.RecordRelay(Mob(612));Check(f.M.HuntTrain[0].TerritoryID==622);
        using var s=new TrainManager(new Host(),new Host(),Path.Combine(f.Dir.Path,"SRankTrain.json"),()=>A);
        s.RecordTrain=true;s.AddMob(Mob(622));s.RecordRelay(Mob(612));Check(s.HuntTrain.Select(m=>m.TerritoryID).SequenceEqual(new uint[]{622,612}));
    }
    private static void SelectionAndBackups()
    {
        using var f=new Fixture();f.M.AddMob(Mob(621));var selected=f.M.HuntTrain[0];var index=0;var events=0;
        f.M.RecordInserted+=(world,inserted,count)=>{Check(world==101);index=TrainVersionFilter.AfterInsertion(index,inserted,count);events++;};
        f.M.AddMob(Mob(612));f.M.AddMob(Mob(620));Check(index==2 && ReferenceEquals(f.M.HuntTrain[index],selected) && events==2);
        Check(f.M.Snapshots.List().Count==0 && TrainVersionFilter.AfterInsertion(0,0,0)==0 && TrainVersionFilter.AfterInsertion(0,1,1)==0);
    }
    private static void OtherVersions()
    {
        using var f=new Fixture();foreach(var map in new uint[]{134,960,622,813,957,612,9999})f.M.AddMob(Mob(map));
        Check(f.M.HuntTrain.Where(m=>Orders[0].Contains(m.TerritoryID)).Select(m=>m.TerritoryID).SequenceEqual(new uint[]{612,622}));
        Check(f.M.HuntTrain.Where(m=>Orders[2].Contains(m.TerritoryID)).Select(m=>m.TerritoryID).SequenceEqual(new uint[]{957,960}));
        Check(f.M.HuntTrain.Where(m=>m.TerritoryID is 134 or 813 or 9999).Select(m=>m.TerritoryID).SequenceEqual(new uint[]{134,813,9999}));
    }
}
