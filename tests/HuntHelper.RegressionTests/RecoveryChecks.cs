using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json;

internal static class RecoveryChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("drag reorder inserts visible records without moving hidden versions or other servers", DragReorder),
        ("drag reorder ignores invalid, stale, foreign and unchanged record sets", InvalidReorder),
        ("S drag order persists and undo restores the previous server order", SReorder),
        ("Sonar failures distinguish name map world coordinates and conflicting instances", Diagnostics),
        ("legacy records never fabricate an origin or local observation time", LegacyTimes),
        ("live observations record real scan provenance without losing Sonar receipt time", ScanTimes),
        ("Sonar receipt does not overwrite a known actual observation time", SonarTimes),
        ("imports stamp import provenance and retain supplied observation evidence", ImportTimes),
        ("record deletion captures a frozen snapshot before mutation", DeleteUndo),
        ("server restoration preserves later edits to unrelated servers", ScopedRestore),
        ("importing a new server can be undone back to an empty destination", ImportUndo),
        ("A and S histories cannot restore each other's files", IndependentHistories),
        ("recovery snapshots and their scope survive a manager restart", Reload),
        ("manual changes retain only ten recovery snapshots and scans create none", Rotation),
        ("snapshot creation failure prevents destructive mutation", BackupFailure),
        ("live record save failure rolls the in-memory mutation back", SaveFailure),
        ("invalid snapshot identifiers and corrupt snapshots preserve current data", InvalidSnapshot),
        ("restoring a backup also backs up the present and preserves manual death guards", RestoreAgain),
    ];
    private static readonly DateTime Seen = new(2026,10,24,10,0,0,DateTimeKind.Utc);
    private static readonly HuntTrainWorld A=new(101,"A服","",0), B=new(102,"B服","",0);
    private static void Check(bool value){if(!value)throw new Exception("Recovery/provenance assertion failed");}
    private static HuntTrainMob Mob(uint id=1,uint world=101) => new("测试",id,134,30,1,"地图",new(20,20),Seen,false,world,world==101?"A服":"B服");
    private sealed class Host:IChatGui,IGameGui
    {
        public void Print(SeString _) => throw new Exception("Unexpected chat send");
        public void OpenMapWithMapLink(MapLinkPayload _) => throw new Exception("Unexpected map UI");
    }
    private sealed record Npc(uint NameId,Vector3 Position):IBattleNpc {public SeString Name=>new(){TextValue="测试"};}
    private sealed class Fixture:IDisposable
    {
        public TemporaryDirectory Dir {get;}=new();
        public string FilePath=>Path.Combine(Dir.Path,"HuntTrain.json");
        public HuntTrainWorld World=A;
        public TrainManager M {get;}
        public Fixture(){M=new(new Host(),new Host(),FilePath,()=>World);}
        public void Dispose(){M.Dispose();Dir.Dispose();}
    }
    private static void DragReorder()
    {
        using var f=new Fixture();foreach(var id in new uint[]{1,90,2,91,3})f.M.AddMob(Mob(id));
        f.World=B;f.M.AddMob(Mob(7));f.M.SelectWorld(101);
        var rows=f.M.HuntTrain.Where(m=>m.MobID<90).ToArray();
        Check(f.M.ReorderTrainMobs(rows,new[]{rows[1],rows[2],rows[0]}));
        Check(f.M.HuntTrain.Select(m=>m.MobID).SequenceEqual(new uint[]{2,90,3,91,1}));
        Check(f.M.GetTrainRecords(102).Single().MobID==7 && ReferenceEquals(f.M.HuntTrain[4],rows[0]));
        var snapshot=f.M.Snapshots.List().Single();Check(snapshot.Operation=="拖拽调整车序");
        Check(f.M.RestoreSnapshot(snapshot.Id) && f.M.HuntTrain.Select(m=>m.MobID).SequenceEqual(new uint[]{1,90,2,91,3}));
    }
    private static void InvalidReorder()
    {
        using var f=new Fixture();f.M.AddMob(Mob(1));f.M.AddMob(Mob(2));var rows=f.M.HuntTrain.ToArray();
        Check(!f.M.ReorderTrainMobs(rows,rows));
        Check(!f.M.ReorderTrainMobs(rows,new[]{rows[0],rows[0]}));
        Check(!f.M.ReorderTrainMobs(rows,new[]{rows[1],Mob(9)}));
        Check(!f.M.ReorderTrainMobs(new[]{rows[1],rows[0]},rows));
        f.World=B;f.M.AddMob(Mob(8));f.M.SelectWorld(102);
        Check(!f.M.ReorderTrainMobs(rows,new[]{rows[1],rows[0]}));
        Check(f.M.Snapshots.List().Count==0 && f.M.GetTrainRecords(101).Select(m=>m.MobID).SequenceEqual(new uint[]{1,2}));
    }
    private static void SReorder()
    {
        using var dir=new TemporaryDirectory();var path=Path.Combine(dir.Path,"SRankTrain.json");
        using(var s=new TrainManager(new Host(),new Host(),path,()=>A,m=>m.MobID>=20))
        {
            s.AddMob(Mob(20));s.AddMob(Mob(21));var rows=s.HuntTrain.ToArray();
            Check(s.ReorderTrainMobs(rows,new[]{rows[1],rows[0]}) && s.Snapshots.List().Count==1);
        }
        using var reload=new TrainManager(new Host(),new Host(),path,()=>A,m=>m.MobID>=20);
        Check(reload.HuntTrain.Select(m=>m.MobID).SequenceEqual(new uint[]{21,20}));
        Check(reload.RestoreSnapshot(reload.Snapshots.List().Single().Id) && reload.HuntTrain[0].MobID==20);
    }
    private static void Diagnostics()
    {
        HuntRelaySpecies[] species=[new("A",1,"测试","Test",134,7)];
        foreach(var (prefix,suffix,map,expected) in new (string,string,HuntRelayMap,string)[]{
            ("Rank A: 未知"," <A服>",new(134,30,new(20,20),42),"名称"),
            ("Rank A: 测试"," <A服>",new(135,30,new(20,20),42),"地图不匹配"),
            ("Rank A: 测试","",new(134,30,new(20,20),42),"缺少明确区服"),
            ("Rank A: 测试"," <未知服>",new(134,30,new(20,20),42),"服务器无法识别"),
            ("Rank A: 测试"," i1 i2 <A服>",new(134,30,new(20,20),42),"分线信息冲突"),
            ("Rank A: 测试"," <A服>",new(134,30,new(float.NaN,20),42),"坐标无效")})
        {
            Check(!HuntRelayParser.TryParse(prefix,suffix,map,species,new[]{A},new Dictionary<char,uint>(),Seen,out var result,out var failure));
            Check(result==null && failure.Contains(expected));
        }
        Check(HuntRelayParser.RankHint("Rank S: 测试 ")=="S" && HuntRelayParser.RankHint("普通聊天")==null);
    }
    private static void LegacyTimes()
    {
        var old=JsonConvert.DeserializeObject<HuntTrainMob>("{\"Name\":\"旧怪\",\"MobID\":1,\"TerritoryID\":134,\"MapID\":30,\"Position\":{\"X\":20,\"Y\":20},\"LastSeenUTC\":\"2026-10-24T10:00:00Z\"}")!;
        Check(old.Source==HuntRecordSource.Unknown && old.LastObservedUtc==null && old.SonarReceivedUtc==null);
        Check(HuntRecordInfo.Describe(old).Contains("历史记录") && HuntRecordInfo.Describe(old).Contains("尚无确认时间"));
        Check(old.WithWorld(A).LastObservedUtc==null);
    }
    private static void ScanTimes()
    {
        using var f=new Fixture();f.M.RecordTrain=true;f.M.RecordRelay(Mob());
        var row=f.M.HuntTrain.Single();var received=row.SonarReceivedUtc;
        f.M.ObserveMob(new Npc(1,new(10,5,20)),true,134,30,1,"地图",100);
        Check(row.Source==HuntRecordSource.Scan && row.LastObservedUtc.HasValue && row.LastObservedUtc==row.LastSeenUTC && row.SonarReceivedUtc==received);
        var scan=new Npc(2,new(10,5,20)).ToHuntTrainMob(134,30,1,"地图",100);
        Check(scan.Source==HuntRecordSource.Scan && scan.LastObservedUtc.HasValue);
    }
    private static void SonarTimes()
    {
        using var f=new Fixture();var original=Mob();original.Source=HuntRecordSource.Scan;original.LastObservedUtc=Seen;original.WorldPosition=new(10,5,20);
        f.M.AddMob(original);f.M.RecordTrain=true;
        var relay=Mob();relay.LastSeenUTC=Seen.AddHours(1);f.M.RecordRelay(relay);
        var row=f.M.HuntTrain.Single();Check(row.LastObservedUtc==Seen && row.SonarReceivedUtc==Seen.AddHours(1) && row.Source==HuntRecordSource.Scan);
        relay.Position=new(35,35);relay.LastSeenUTC=Seen.AddHours(2);relay.SonarReceivedUtc=relay.LastSeenUTC;f.M.RecordRelay(relay);
        Check(row.Source==HuntRecordSource.Sonar && row.WorldPosition==null && row.LastObservedUtc==Seen);
    }
    private static void ImportTimes()
    {
        using var f=new Fixture();var row=Mob();row.Source=HuntRecordSource.Scan;row.LastObservedUtc=Seen;row.RecordedAtUtc=Seen;
        f.M.Import(new[]{row});f.M.SetImportWorld(B);var preview=f.M.ImportedTrain.Single();
        Check(preview.Source==HuntRecordSource.Import && preview.ImportedAtUtc>Seen && preview.LastObservedUtc==Seen && preview.LastSeenUTC==Seen);
        Check(row.Source==HuntRecordSource.Scan && row.ImportedAtUtc==null);
    }
    private static void DeleteUndo()
    {
        using var f=new Fixture();f.M.AddMob(Mob());f.M.AddMob(Mob(2));var original=f.M.HuntTrain[0];
        f.M.TrainRemove(original);var snapshot=f.M.Snapshots.List().Single();original.Name="修改了旧对象";
        Check(snapshot.Records.Count==2 && snapshot.Records[0].Name=="测试");
        Check(f.M.RestoreSnapshot(snapshot.Id) && f.M.HuntTrain.Count==2 && f.M.HuntTrain[0].Name=="测试");
    }
    private static void ScopedRestore()
    {
        using var f=new Fixture();f.M.AddMob(Mob());f.World=B;f.M.AddMob(Mob(2));f.M.SelectWorld(101);
        f.M.DeleteWorld(101);var snapshot=f.M.Snapshots.List().Single();
        f.M.SelectWorld(102);f.M.AddMob(Mob(3));f.M.SetDead(0,true);f.M.RecordTrain=true;
        Check(f.M.RestoreSnapshot(snapshot.Id));
        Check(f.M.GetTrainRecords(101).Count==1 && f.M.GetTrainRecords(102).Count==2 && f.M.GetTrainRecords(102)[0].Dead && f.M.RecordTrain);
    }
    private static void ImportUndo()
    {
        using var f=new Fixture();f.M.AddMob(Mob());f.M.Import(new[]{Mob(2,102)});Check(f.M.ImportTrainAll());
        var snapshot=f.M.Snapshots.List().Single();Check(snapshot.Worlds.SequenceEqual(new uint[]{102}) && snapshot.Records.Count==0);
        Check(f.M.RestoreSnapshot(snapshot.Id) && f.M.GetTrainRecords(102).Count==0 && f.M.GetTrainRecords(101).Count==1);
    }
    private static void IndependentHistories()
    {
        using var dir=new TemporaryDirectory();using var a=new TrainManager(new Host(),new Host(),Path.Combine(dir.Path,"HuntTrain.json"),()=>A);
        using var s=new TrainManager(new Host(),new Host(),Path.Combine(dir.Path,"SRankTrain.json"),()=>A,m=>m.MobID==20);
        a.AddMob(Mob());s.AddMob(Mob(20));a.SetDead(0,true);s.SetDead(0,true);
        Check(!s.RestoreSnapshot(a.Snapshots.List()[0].Id) && s.HuntTrain.Single().Dead);
        Check(s.RestoreSnapshot(s.Snapshots.List()[0].Id) && !s.HuntTrain.Single().Dead && a.HuntTrain.Single().Dead);
    }
    private static void Reload()
    {
        using var dir=new TemporaryDirectory();var file=Path.Combine(dir.Path,"HuntTrain.json");string id;
        using(var first=new TrainManager(new Host(),new Host(),file,()=>A)){first.AddMob(Mob());first.DeleteWorld(101);id=first.Snapshots.List().Single().Id;}
        using var reload=new TrainManager(new Host(),new Host(),file,()=>A);
        Check(reload.Snapshots.List().Single().Id==id && reload.RestoreSnapshot(id) && reload.HuntTrain.Count==1);
    }
    private static void Rotation()
    {
        using var f=new Fixture();f.M.AddMob(Mob());Check(f.M.Snapshots.List().Count==0);
        for(var i=0;i<22;i++)f.M.SetDead(0,i%2==0);
        Check(f.M.Snapshots.List().Count==10 && Directory.GetFiles(Path.Combine(f.Dir.Path,"Backups","HuntTrain"),"*.json").Length==10);
    }
    private static void BackupFailure()
    {
        using var f=new Fixture();f.M.AddMob(Mob());var original=File.ReadAllText(f.FilePath);
        File.WriteAllText(Path.Combine(f.Dir.Path,"Backups"),"阻止目录创建");f.M.DeleteWorld(101);
        Check(f.M.HuntTrain.Count==1 && File.ReadAllText(f.FilePath)==original && f.M.ActionError!.Contains("备份失败"));
    }
    private static void SaveFailure()
    {
        using var f=new Fixture();f.M.AddMob(Mob());
        using(var locked=File.Open(f.FilePath,FileMode.Open,FileAccess.Read,FileShare.None))
        { f.M.DeleteWorld(101);Check(f.M.HuntTrain.Count==1 && f.M.ActionError!.Contains("保存失败")); }
        Check(JsonConvert.DeserializeObject<List<HuntTrainMob>>(File.ReadAllText(f.FilePath))!.Count==1);
    }
    private static void InvalidSnapshot()
    {
        using var f=new Fixture();f.M.AddMob(Mob());f.M.SetDead(0,true);var id=f.M.Snapshots.List()[0].Id;
        Check(!f.M.RestoreSnapshot("../HuntTrain") && f.M.HuntTrain.Single().Dead);
        File.WriteAllText(Path.Combine(f.Dir.Path,"Backups","HuntTrain",id+".json"),"corrupt");
        Check(!f.M.RestoreSnapshot(id) && f.M.HuntTrain.Single().Dead);
    }
    private static void RestoreAgain()
    {
        using var f=new Fixture();f.M.AddMob(Mob());f.M.SetDead(0,true);var id=f.M.Snapshots.List()[0].Id;
        var manual=0;f.M.ManualDeadStateChanged+=_=>manual++;f.M.RecordTrain=true;
        Check(f.M.RestoreSnapshot(id) && !f.M.HuntTrain.Single().Dead && !f.M.RecordTrain && manual==1);
        var redo=f.M.Snapshots.List()[0];Check(redo.Operation=="恢复备份前" && f.M.RestoreSnapshot(redo.Id) && f.M.HuntTrain.Single().Dead);
    }
}
