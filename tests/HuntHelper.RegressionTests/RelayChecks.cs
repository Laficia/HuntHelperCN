using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class RelayChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("Sonar clicked A and S messages preserve explicit source world and map coordinates", ParsedRanks),
        ("Sonar English aliases and cross-world icons resolve without name-language assumptions", Aliases),
        ("Sonar instance glyphs and text resolve distinct lines and reject contradictory lines", Instances),
        ("Sonar missing unknown or ambiguous worlds never fall back to the visited server", InvalidWorlds),
        ("Sonar rejects wrong maps nonfinite coordinates unknown monsters and unsupported ranks", InvalidMessages),
        ("same localized monster names are disambiguated by territory", AmbiguousNames),
        ("tagged formats cannot match an empty alias or unidentified name", TaggedNames),
        ("legacy A settings stay flat while S settings persist independently", Settings),
        ("S rosters use actual S species counts and S version labels", Catalog),
        ("local S recording uses visited server and remains isolated from A storage", LocalRecording),
        ("relay recording is opt-in and preserves message server while browsing another server", RelayWorlds),
        ("repeated relay clicks deduplicate by monster server and instance", Deduplication),
        ("relay updates retain nearby scanned height and manual dead state but clear stale height", Heights),
        ("S share import and persistence filter unrelated ranks without changing A records", FilteredImport),
    ];
    private static readonly DateTime Seen = new(2026,10,24,13,0,0,DateTimeKind.Utc);
    private static readonly HuntTrainWorld[] Worlds = [new(101,"银泪湖","豆豆柴",0),new(102,"红茶川","豆豆柴",0)];
    private static readonly HuntRelaySpecies[] Species = [new("A",10,"女王蜂","Queen Hawk",134,7),new("S",20,"测试S怪","Test S",134,7)];
    private static readonly Dictionary<char,uint> Glyphs = new() { ['\ue0b1']=1,['\ue0b2']=2 };
    private static HuntRelayResult? Parse(string prefix="Rank S: 测试S怪 ", string suffix=" <银泪湖>",
        HuntRelayMap? map=null, IReadOnlyList<HuntRelaySpecies>? species=null, bool tagged=false)
    {
        HuntRelayParser.TryParse(prefix,suffix,map??new(134,30,new(18.14f,12.79f),42),species??Species,Worlds,Glyphs,Seen,out var result,tagged);
        return result;
    }
    private static void Check(bool value) { if(!value)throw new Exception("Relay/S module assertion failed"); }
    private static void ParsedRanks()
    {
        var s=Parse()!; Check(s.Rank=="S" && s.Mob.MobID==20 && s.Mob.WorldID==101 && s.Mob.Instance==0 &&
            s.Mob.Position==new Vector2(18.14f,12.79f) && s.Mob.WorldPosition==null && s.Mob.LastSeenUTC==Seen);
        var a=Parse("Rank A: 女王蜂 "," <红茶川>")!;Check(a.Rank=="A" && a.Mob.MobID==10 && a.Mob.WorldID==102);
    }
    private static void Aliases() => Check(Parse("Rank S: Test S "," <\ue0b0红茶川>")?.Mob.WorldID==102 &&
        Parse("Ｓ：测试S怪 "," ＜银泪湖＞")?.Rank=="S");
    private static void Instances()
    {
        Check(Parse(suffix:" \ue0b2 <银泪湖>")?.Mob.Instance==2);
        Check(Parse(suffix:" i2 <银泪湖>")?.Mob.Instance==2);
        Check(Parse(suffix:" 2线 <银泪湖>")?.Mob.Instance==2);
        Check(Parse(suffix:" \ue0b1 i2 <银泪湖>")==null);
    }
    private static void InvalidWorlds()
    {
        foreach(var text in new[]{"", " i1", " <不存在>"," <银泪湖> <红茶川>"})Check(Parse(suffix:text)==null);
    }
    private static void InvalidMessages()
    {
        foreach(var text in new[]{"Rank B: 测试S怪", "Rank SS: 测试S怪", "FATE: 测试S怪", "Rank S: 不存在"})
        { Check(Parse(text)==null); Check(Parse(text,tagged:true)==null); }
        foreach(var map in new HuntRelayMap[]{new(135,30,new(18,12),42),new(134,0,new(18,12),42),new(134,30,new(float.NaN,12),42),
            new(134,30,new(0,12),42),new(134,30,new(43,12),42)})Check(Parse(map:map)==null);
    }
    private static void AmbiguousNames()
    {
        HuntRelaySpecies[] list=[..Species,new("S",21,"测试S怪","Another S",135,6)];
        Check(Parse(species:list)?.Mob.MobID==20);
        Check(Parse(species:[..Species,new("S",22,"测试S怪","Other",134,6)])==null);
    }
    private static void TaggedNames()
    {
        var list=new[]{new HuntRelaySpecies("S",20,"测试S怪","",134,7)};
        Check(Parse("[Sonar] 发现了测试S怪",species:list,tagged:true)?.Rank=="S");
        Check(Parse("[Sonar] 随便的消息",species:list,tagged:true)==null);
        Check(Parse("发现了测试S怪",species:list)==null);
    }
    private static void Settings()
    {
        var config=JsonConvert.DeserializeObject<Configuration>("{\"HuntTrainShowMissingRoster\":true,\"HuntTrainWorldTabOrder\":[102,101],\"HuntTrainWindowSize\":{\"X\":700,\"Y\":400}}")!;
        Check(config.HuntTrainShowMissingRoster && !config.SRankTrain.HuntTrainShowMissingRoster && config.HuntTrainWindowSize.X==700);
        config.SRankTrain.HuntTrainWorldTabOrder.Add(101);config.SRankTrain.HuntTrainCaptureSonar=false;
        var json=JObject.Parse(JsonConvert.SerializeObject(config));
        Check(json["HuntTrainWorldTabOrder"]!.Values<uint>().SequenceEqual(new uint[]{102,101}) && json["SRankTrain"]!["HuntTrainWorldTabOrder"]!.Values<uint>().SequenceEqual(new uint[]{101}));
        var reload=json.ToObject<Configuration>()!; Check(reload.HuntTrainCaptureSonar && !reload.SRankTrain.HuntTrainCaptureSonar && reload.HuntSDeathMonitoring);
    }
    private static void Catalog()
    {
        var entries=new List<HuntRosterEntry>();
        foreach(var (version,prefix) in new[]{(7,"DT"),(6,"EW"),(5,"ShB"),(4,"SB"),(3,"HW"),(2,"ARR")})
        {
            var rows=JArray.Parse(File.ReadAllText(Path.Combine("HuntHelper","Data",prefix+"-S.json")))
                .Where(row=>row.Value<string>("Rank")=="S").ToArray();
            Check(rows.Length==(version==2?17:6));
            entries.AddRange(rows.Select(r=>new HuntRosterEntry(version,r.Value<uint>("ModelID"),r.Value<string>("Name")!)));
        }
        var roster=new HuntRoster(entries);var filter=new TrainVersionFilter(m=>entries.FirstOrDefault(e=>e.NameId==m.MobID)?.Version,roster,"S");
        Check(filter.Groups([]).Where(g=>g.Version.HasValue).All(g=>g.Label.EndsWith("S")));
        filter.ShowEmptyRosters=false;Check(filter.Groups([]).Count==0);
    }
    private static HuntTrainMob Mob(uint id=20,uint world=101,uint instance=0,Vector2? pos=null,Vector3? height=null,bool dead=false) =>
        new("测试S怪",id,134,30,instance,"地图",pos??new(18.14f,12.79f),Seen,dead,world,world==101?"银泪湖":"红茶川","豆豆柴",height);
    private sealed class Host : IChatGui, IGameGui
    {
        public void Print(SeString _) => throw new Exception("Unexpected chat send");
        public void OpenMapWithMapLink(MapLinkPayload _) => throw new Exception("Unexpected map UI");
    }
    private sealed record Npc(uint NameId,Vector3 Position):IBattleNpc {public SeString Name=>new(){TextValue="测试S怪"};}
    private static TrainManager Manager(string file,Func<HuntTrainWorld>? world=null, bool s=true) =>
        new(new Host(),new Host(),file,world??(()=>Worlds[0]),s?m=>m.MobID==20:null);
    private static void LocalRecording()
    {
        using var dir=new TemporaryDirectory();var visited=Worlds[0];
        using var a=Manager(Path.Combine(dir.Path,"HuntTrain.json"),s:false);
        using var s=Manager(Path.Combine(dir.Path,"SRankTrain.json"),()=>visited);
        var npc=new Npc(20,new(100,20,200));
        s.SelectWorld(102);s.ObserveMob(npc,true,134,30,1,"地图",100);Check(s.GetAllTrainRecords().Count==0);
        s.RecordTrain=true;s.ObserveMob(npc,true,134,30,1,"地图",100);
        Check(s.SelectedWorldId==102 && s.GetTrainRecords(101).Single().WorldPosition==npc.Position && a.GetAllTrainRecords().Count==0);
        s.AddMob(Mob(10));Check(s.GetAllTrainRecords().Count==1);
        visited=Worlds[1];s.ObserveMob(npc,true,134,30,1,"地图",100);Check(s.GetTrainRecords(102).Count==1 && s.GetTrainRecords(101).Count==1);
    }
    private static void RelayWorlds()
    {
        using var dir=new TemporaryDirectory();using var s=Manager(Path.Combine(dir.Path,"SRankTrain.json"));
        Check(s.RecordRelay(Mob(world:102))==RelayRecordResult.Ignored);
        s.RecordTrain=true;s.SelectWorld(101);Check(s.RecordRelay(Mob(world:102))==RelayRecordResult.Added);
        Check(s.SelectedWorldId==101 && s.GetTrainRecords(102).Single().WorldID==102 && s.GetTrainRecords(101).Count==0);
        Check(s.RecordRelay(Mob(world:0))==RelayRecordResult.Ignored && s.RecordRelay(Mob(10))==RelayRecordResult.Ignored);
    }
    private static void Deduplication()
    {
        using var dir=new TemporaryDirectory();using var s=Manager(Path.Combine(dir.Path,"SRankTrain.json"));s.RecordTrain=true;
        Check(s.RecordRelay(Mob())==RelayRecordResult.Added && s.RecordRelay(Mob())==RelayRecordResult.Updated);
        s.RecordRelay(Mob(instance:2));s.RecordRelay(Mob(world:102));
        Check(s.GetTrainRecords(101).Count==2 && s.GetTrainRecords(102).Count==1);
    }
    private static void Heights()
    {
        using var dir=new TemporaryDirectory();using var s=Manager(Path.Combine(dir.Path,"SRankTrain.json"));s.RecordTrain=true;
        s.AddMob(Mob(height:new(100,20,200),dead:true));
        s.RecordRelay(Mob(pos:new(18.15f,12.8f)));var row=s.GetTrainRecords(101).Single();
        Check(row.WorldPosition==new Vector3(100,20,200) && row.Dead);
        s.RecordRelay(Mob(pos:new(25,30)));Check(row.WorldPosition==null && row.Position==new Vector2(25,30) && row.Dead);
    }
    private static void FilteredImport()
    {
        using var dir=new TemporaryDirectory();var file=Path.Combine(dir.Path,"SRankTrain.json");
        using(var s=Manager(file))
        {
            s.Import(ExportImport.Export(new List<HuntTrainMob>{Mob(10),Mob(world:102)}));
            Check(s.ImportedTrain.Count==1 && s.ImportedTrain[0].MobID==20);s.ImportTrainAll();
            Check(s.GetTrainRecords(102).Count==1);
        }
        using(var s=Manager(file))Check(s.GetTrainRecords(102).Single().MobID==20 && !s.RecordTrain);
        File.WriteAllText(file,JsonConvert.SerializeObject(new[]{Mob(10),Mob()}));
        using var filtered=Manager(file);Check(filtered.GetAllTrainRecords().Single().MobID==20);
    }
}
