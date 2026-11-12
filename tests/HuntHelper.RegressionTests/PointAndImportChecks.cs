using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json;

internal static class PointAndImportChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("S coordinate macros substitute record world and local only on the actual visited world", ServerMacro),
        ("A and S presets persist independently and defaults match their own rank", IndependentMacros),
        ("Macro flag parts bind to the clicked record without replacing injected names as template tokens", FlagParts),
        ("Coordinate macros reject nonchat commands and control injection before sending any line", InvalidMacros),
        ("Multi-line macros preserve line sequence and reject more than fifteen", MultiLine),
        ("Same-map imported A hunts start with the closer hunt from the player entry", PairOrder),
        ("Map route optimization agrees with exhaustive enumeration on a nontrivial group", ExactRoute),
        ("Import optimizer isolates worlds maps and instances without moving their slots", GroupIsolation),
        ("Equal-distance order remains stable and invalid coordinates are untouched", StableRoutes),
        ("Confirmed replace import optimizes, persists, and supports one scoped undo", ReplaceImport),
        ("Merge import optimizes only affected groups and later manual drag remains authoritative", MergeImport),
        ("S imports keep original order", SImport),
        ("Sonar death parser accepts actual native and clicked terminal statuses only", DeathStatuses),
        ("Sonar kill marks an existing exact record while recording is disabled", DeathMatch),
        ("Sonar kill cannot mark another world map instance or create missing records", DeathIsolation),
        ("Missing instance on an instanced hunt is rejected and duplicate records are ambiguous", DeathAmbiguity),
        ("Sonar kill preserves coordinates provenance and real observation times on disk", DeathProvenance),
    ];
    private static readonly DateTime Seen = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static HuntTrainMob Mob(uint id = 1, float x = 20, uint world = 101, uint map = 30, uint line = 0) =>
        new("女王蜂", id, 134, map, line, "地图", new(x, 20), Seen, false, world, world == 101 ? "银泪湖" : "水晶塔",
            worldPosition: new Vector3(x, 5, 20), source: HuntRecordSource.Scan, recordedAtUtc: Seen, lastObservedUtc: Seen);
    private static void Check(bool ok) { if (!ok) throw new Exception("Point/import/death assertion failed"); }
    private static string Expand(string t, HuntTrainMob? m = null, string rank = "S", uint world = 101) =>
        string.Join("\n", HuntPointMacro.Expand(t, m ?? Mob(), rank, world).Select(l => string.Concat(l.Select(p => p.IsFlag ? "[MAP]" : p.Text))));
    private static void ServerMacro()
    {
        var settings = new HuntPointMacroSettings();
        Check(Expand(settings.S, Mob(world: 102)) == "/sh 下一站【水晶塔】女王蜂[MAP]");
        Check(Expand(settings.S).Contains("【本地】"));
        Check(Expand(settings.S, Mob(world: 102), world: 102).Contains("【本地】"));
        Check(Expand(settings.S, Mob(), world: 102).Contains("【银泪湖】"));
    }
    private static void IndependentMacros()
    {
        var c = new Configuration(); var originalS = c.PointMacros.S; c.PointMacros.A = "/echo A<flag>";
        var copy = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(c))!;
        Check(copy.PointMacros.A == "/echo A<flag>" && copy.PointMacros.S == originalS);
    }
    private static void FlagParts()
    {
        var m = Mob(world: 102, line: 2); m.Name = "名字<服务器>";
        var lines = HuntPointMacro.Expand("/p <名字><flag><服务器><分线>", m, "A", 101);
        Check(lines[0].Count(p => p.IsFlag) == 1 && Expand("/p <名字>", m) == "/p 名字<服务器>");
        Check(Expand("/p <服务器><分线>", m) == "/p 水晶塔2线");
    }
    private static void InvalidMacros()
    {
        foreach (var t in new[] { "/tp 1 2 3", "/vnav moveto 1 2 3", "/p ok\n/ac sprint", "/sh hi\u0000evil", "", "/sh" })
        { var rejected = false; try { Expand(t); } catch (ArgumentException) { rejected = true; } Check(rejected); }
    }
    private static void MultiLine()
    {
        Check(Expand("/echo 第一行\r\n\r\n/p 第二行") == "/echo 第一行\n/p 第二行");
        var rejected = false; try { Expand(string.Join('\n', Enumerable.Repeat("/echo hi", 16))); } catch (ArgumentException) { rejected = true; } Check(rejected);
    }
    private static IReadOnlyList<HuntTrainMob> Order(params HuntTrainMob[] mobs) => HuntImportOrder.Optimize(mobs, _ => true, _ => new[] { new Vector2(0, 20) });
    private static void PairOrder() { var far = Mob(1, 30); var near = Mob(2, 5); Check(Order(far, near).SequenceEqual(new[] { near, far })); }
    private static float Cost(IReadOnlyList<HuntTrainMob> rows) => Vector2.Distance(new(0, 20), rows[0].Position) + Enumerable.Range(1, rows.Count - 1).Sum(i => Vector2.Distance(rows[i - 1].Position, rows[i].Position));
    private static IEnumerable<T[]> Permutations<T>(T[] values) => values.Length == 0 ? new[] { Array.Empty<T>() } :
        values.SelectMany((v, i) => Permutations(values.Where((_, j) => j != i).ToArray()).Select(t => new[] { v }.Concat(t).ToArray()));
    private static void ExactRoute()
    {
        var rows = new[] { Mob(1, 20), Mob(2, 4), Mob(3, 30), Mob(4, 7), Mob(5, 16) };
        rows[2].Position = new(30, 4); rows[4].Position = new(16, 9);
        var best = Permutations(rows).Min(Cost); Check(Math.Abs(Cost(Order(rows)) - best) < .001f);
    }
    private static void GroupIsolation()
    {
        var a = Mob(1, 30); var b = Mob(2, 5); var foreign = Mob(8, world: 102); var map = Mob(9, map: 31); var line = Mob(10, line: 2);
        var ordered = Order(a, foreign, map, line, b); Check(ordered.SequenceEqual(new[] { b, foreign, map, line, a }));
        Check(a.Source == HuntRecordSource.Scan && a.WorldPosition == new Vector3(30, 5, 20));
    }
    private static void StableRoutes()
    {
        var a = Mob(1); var b = Mob(2); Check(Order(a, b).SequenceEqual(new[] { a, b }));
        b.Position = new(float.NaN, 20); Check(Order(a, b).SequenceEqual(new[] { a, b }));
    }
    private sealed class Host : IChatGui, IGameGui
    { public void Print(SeString _) => throw new Exception("Unexpected chat"); public void OpenMapWithMapLink(MapLinkPayload _) => throw new Exception("Unexpected map"); }
    private static TrainManager Manager(string path, bool optimize = true)
    {
        var m = new TrainManager(new Host(), new Host(), path, () => new HuntTrainWorld(101, "银泪湖", "", 0));
        if (optimize) { m.ImportOrderEligible = _ => true; m.ImportEntryPoints = _ => new[] { new Vector2(0, 20) }; } return m;
    }
    private static void ReplaceImport()
    {
        using var d = new TemporaryDirectory(); var path = Path.Combine(d.Path, "a.json"); using var m = Manager(path);
        m.AddMob(Mob(9)); m.Import(new[] { Mob(1, 30), Mob(2, 5) }); Check(m.HuntTrain.Single().MobID == 9);
        Check(m.ImportTrainAll() && m.HuntTrain.Select(x => x.MobID).SequenceEqual(new uint[] { 2, 1 }));
        var snapshot = m.Snapshots.List().Single(); using var reload = Manager(path); Check(reload.HuntTrain[0].MobID == 2);
        Check(m.RestoreSnapshot(snapshot.Id) && m.HuntTrain.Single().MobID == 9);
    }
    private static void MergeImport()
    {
        using var d = new TemporaryDirectory(); using var m = Manager(Path.Combine(d.Path, "a.json"));
        m.AddMob(Mob(1, 30)); m.AddMob(Mob(8, 30, map: 31)); m.AddMob(Mob(9, 5, map: 31));
        m.Import(new[] { Mob(2, 5) }); Check(m.ImportTrainNew(true));
        Check(m.HuntTrain.Select(x => x.MobID).SequenceEqual(new uint[] { 2, 8, 9, 1 }));
        var visible = m.HuntTrain.Where(x => x.MapID == 30).ToArray(); Check(m.ReorderTrainMobs(visible, visible.Reverse().ToArray()));
        Check(m.HuntTrain.Select(x => x.MobID).SequenceEqual(new uint[] { 1, 8, 9, 2 }));
    }
    private static void SImport()
    { using var d = new TemporaryDirectory(); using var m = Manager(Path.Combine(d.Path, "s.json"), false); m.Import(new[] { Mob(1, 30), Mob(2, 5) }); Check(m.ImportTrainAll() && m.HuntTrain[0].MobID == 1); }
    private static void DeathStatuses()
    {
        foreach (var end in new[] { " was just killed", " DEAD", " 已被击杀", " 已死亡。" }) Check(SonarKillMessage.TryStrip(" i2 <银泪湖>" + end, out var loc) && loc == " i2 <银泪湖>");
        foreach (var text in new[] { " i2 <银泪湖>", " <银泪湖> not dead", " <银泪湖> LastKilled: yesterday", "dead <银泪湖>", " <银泪湖> was just killed yesterday" }) Check(!SonarKillMessage.TryStrip(text, out _));
    }
    private static void DeathMatch()
    {
        using var d = new TemporaryDirectory(); using var m = Manager(Path.Combine(d.Path, "a.json")); m.AddMob(Mob());
        Check(!m.RecordTrain && m.MarkSonarKilled(Mob(), Seen.AddMinutes(1), out _) && m.HuntTrain[0].Dead && m.Snapshots.List().Count == 0);
    }
    private static void DeathIsolation()
    {
        using var d = new TemporaryDirectory(); using var m = Manager(Path.Combine(d.Path, "a.json")); m.AddMob(Mob());
        foreach (var relay in new[] { Mob(world: 102), Mob(map: 31), Mob(line: 2), Mob(8) }) Check(!m.MarkSonarKilled(relay, Seen.AddMinutes(1), out _));
        Check(!m.HuntTrain[0].Dead && m.GetAllTrainRecords().Count == 1);
    }
    private static void DeathAmbiguity()
    {
        using var d = new TemporaryDirectory(); using var m = Manager(Path.Combine(d.Path, "a.json")); m.AddMob(Mob(line: 1));
        Check(!m.MarkSonarKilled(Mob(), Seen.AddMinutes(1), out _)); m.HuntTrain.Add(Mob(line: 1));
        Check(!m.MarkSonarKilled(Mob(line: 1), Seen.AddMinutes(1), out _) && m.HuntTrain.All(x => !x.Dead));
    }
    private static void DeathProvenance()
    {
        using var d = new TemporaryDirectory(); var path = Path.Combine(d.Path, "a.json"); using var m = Manager(path); m.AddMob(Mob());
        Check(!m.MarkSonarKilled(Mob(), Seen.AddSeconds(-1), out _)); Check(m.MarkSonarKilled(Mob(), Seen.AddMinutes(1), out _));
        using var reload = Manager(path); var row = reload.HuntTrain[0];
        Check(row.Dead && row.SonarKilledUtc == Seen.AddMinutes(1) && row.Source == HuntRecordSource.Scan && row.LastObservedUtc == Seen && row.WorldPosition == new Vector3(20, 5, 20));
    }
}
