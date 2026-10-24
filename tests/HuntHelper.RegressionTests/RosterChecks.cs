using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json.Linq;
using System.Numerics;

internal static class RosterChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("each requested expansion has its real twelve-species A-rank roster", RealCatalog),
        ("empty servers retain selectable 7A 6A 5A 4A rosters at zero of twelve", Empty),
        ("discovered species replace only their own missing slot", Discovery),
        ("roster progress remains isolated between servers", Worlds),
        ("multiple instances count a species once while retaining all records", Instances),
        ("deleting a real record restores its missing slot and dead records remain found", Delete),
        ("roster display cannot fabricate exportable records or navigation positions", Sharing),
        ("missing-roster visibility defaults off for new and legacy configs and persists explicit choices", VisibilitySetting),
        ("only the instanced map expands its expected roster", MixedMaps),
        ("one instance sighting cannot fill a different instance slot", InstanceDiscovery),
        ("instance inference stays isolated by server and territory", InstanceIsolation),
        ("live non-instanced observations override stale historical instance counts", LiveSingle),
        ("unknown-instance imports cannot fill numbered instance slots", UnknownInstance),
        ("per-map overrides persist and automatic mode removes only that override", InstanceOverrides),
        ("deleting a discovered instance restores only its own placeholder", DeleteInstance)
    ];
    private static HuntRoster Catalog()
    {
        var entries=new List<HuntRosterEntry>();
        foreach(var (version,prefix) in new[]{(7,"DT"),(6,"EW"),(5,"ShB"),(4,"SB")})
            foreach(var row in JArray.Parse(File.ReadAllText(Path.Combine("HuntHelper","Data",prefix+"-A.json"))))
                entries.Add(new HuntRosterEntry(version,row.Value<uint>("ModelID"),row.Value<string>("Name")!));
        return new HuntRoster(entries);
    }
    private static HuntTrainMob Found(HuntRosterEntry entry,uint world=101,uint instance=1) => new(entry.Name,entry.NameId,
        134,30,instance,"Map",new Vector2(20,20),DateTime.UtcNow,false,world,$"World {world}","",new Vector3(10,0,10));
    private static TrainVersionFilter Filter(HuntRoster roster) => new(m=>roster.Entries.FirstOrDefault(e=>e.NameId==m.MobID)?.Version,roster);
    private static void Check(bool value) { if(!value)throw new InvalidOperationException("Roster assertion failed."); }
    private static void VisibilitySetting()
    {
        Check(!new HuntHelper.Configuration().HuntTrainShowMissingRoster);
        var legacy = Newtonsoft.Json.JsonConvert.DeserializeObject<HuntHelper.Configuration>("{}")!;
        Check(!legacy.HuntTrainShowMissingRoster);
        foreach (var enabled in new[] { true, false })
        {
            legacy.HuntTrainShowMissingRoster = enabled;
            var saved = Newtonsoft.Json.JsonConvert.SerializeObject(legacy);
            Check(Newtonsoft.Json.JsonConvert.DeserializeObject<HuntHelper.Configuration>(saved)!.HuntTrainShowMissingRoster == enabled);
        }
    }

    private static HuntRoster MappedCatalog() => new(new[]
    {
        new HuntRosterEntry(7, 1, "Map A One", 100), new HuntRosterEntry(7, 2, "Map A Two", 100),
        new HuntRosterEntry(7, 3, "Map B One", 200), new HuntRosterEntry(7, 4, "Map B Two", 200)
    });
    private static HuntTrainMob Sighting(uint id, uint map, uint line, uint world = 101) =>
        new("Observed", id, map, 1, line, "Map", Vector2.Zero, DateTime.UtcNow, false, world, "World");
    private static HuntRosterSnapshot Snapshot(HuntInstanceLayout layout, IReadOnlyList<HuntTrainMob> records, uint world = 101) =>
        MappedCatalog().Snapshot(world, 7, records, map => layout.Instances(world, map, records));
    private static void MixedMaps()
    {
        var layout = new HuntInstanceLayout(new());
        layout.SetMode(101, 100, 3);
        var snapshot = Snapshot(layout, Array.Empty<HuntTrainMob>());
        Check(snapshot.Progress == new HuntRosterProgress(0, 8));
        Check(snapshot.Missing.Count(s => s.TerritoryId == 100) == 6);
        Check(snapshot.Missing.Where(s => s.TerritoryId == 200).All(s => s.Instance == 0));
    }
    private static void InstanceDiscovery()
    {
        var layout = new HuntInstanceLayout(new()); layout.SetMode(101, 100, 3);
        var rows = new[] { Sighting(1, 100, 1) };
        var snapshot = Snapshot(layout, rows);
        Check(snapshot.Progress == new HuntRosterProgress(1, 8));
        Check(snapshot.Missing.Where(s => s.Entry.NameId == 1).Select(s => s.Instance).SequenceEqual(new uint[] { 2, 3 }));
        rows[0].Dead = true; Check(Snapshot(layout, rows).Progress.Found == 1);
    }
    private static void InstanceIsolation()
    {
        var layout = new HuntInstanceLayout(new()); layout.Observe(101, 100, 3);
        var rows = new[] { Sighting(1, 100, 2), Sighting(1, 100, 9, 102), Sighting(3, 200, 0) };
        Check(layout.Instances(101, 100, rows).SequenceEqual(new uint[] { 1, 2, 3 }));
        Check(layout.Instances(101, 200, rows).SequenceEqual(new uint[] { 0 }));
        Check(layout.Instances(103, 100, rows).SequenceEqual(new uint[] { 0 }));
        Check(Snapshot(layout, rows).Progress == new HuntRosterProgress(2, 8));
    }
    private static void LiveSingle()
    {
        var layout = new HuntInstanceLayout(new()); var rows = new[] { Sighting(1, 100, 3) };
        Check(layout.Instances(101, 100, rows).Count == 3);
        layout.Observe(101, 100, 0);
        Check(layout.Instances(101, 100, rows).SequenceEqual(new uint[] { 0 }));
        Check(rows.Length == 1 && rows[0].Instance == 3);
        layout.SetMode(101, 100, 2); Check(layout.Instances(101, 100, rows).Count == 2);
    }
    private static void UnknownInstance()
    {
        var layout = new HuntInstanceLayout(new()); layout.SetMode(101, 100, 2);
        var rows = new[] { Sighting(1, 100, 0) };
        Check(Snapshot(layout, rows).Progress.Found == 0);
        Check(Snapshot(layout, rows).Missing.Count(s => s.Entry.NameId == 1) == 2);
        Check(rows[0].Instance == 0 && rows.Length == 1);
    }
    private static void InstanceOverrides()
    {
        var config = new HuntHelper.Configuration(); var layout = new HuntInstanceLayout(config.HuntTrainMapInstanceCounts);
        layout.SetMode(101, 100, 3); layout.SetMode(101, 200, 1); layout.SetMode(102, 100, 2);
        var restored = Newtonsoft.Json.JsonConvert.DeserializeObject<HuntHelper.Configuration>(Newtonsoft.Json.JsonConvert.SerializeObject(config))!;
        var loaded = new HuntInstanceLayout(restored.HuntTrainMapInstanceCounts);
        Check(loaded.Mode(101, 100) == 3 && loaded.Mode(101, 200) == 1 && loaded.Mode(102, 100) == 2);
        loaded.SetMode(101, 100, 0);
        Check(loaded.Mode(101, 100) == 0 && loaded.Mode(101, 200) == 1 && loaded.Mode(102, 100) == 2);
        loaded.SetMode(101, 200, 0); Check(!restored.HuntTrainMapInstanceCounts.ContainsKey(101));
    }
    private static void DeleteInstance()
    {
        var layout = new HuntInstanceLayout(new()); layout.SetMode(101, 100, 2);
        var rows = new List<HuntTrainMob> { Sighting(1, 100, 1), Sighting(1, 100, 2) };
        Check(Snapshot(layout, rows).Progress.Found == 2);
        rows.RemoveAt(0);
        var missing = Snapshot(layout, rows).Missing.Where(s => s.Entry.NameId == 1).ToList();
        Check(missing.Count == 1 && missing[0].Instance == 1 && rows[0].Instance == 2);
        var automatic = new HuntInstanceLayout(new());
        var automaticRows = new List<HuntTrainMob> { Sighting(1, 100, 1), Sighting(1, 100, 2) };
        Check(Snapshot(automatic, automaticRows).Progress.Total == 6);
        automaticRows.RemoveAt(1);
        Check(Snapshot(automatic, automaticRows).Missing.Any(s => s.Entry.NameId == 1 && s.Instance == 2));
    }
    private static void RealCatalog()
    {
        var catalog=Catalog();Check(catalog.Entries.Count==48);
        foreach(var version in new[]{7,6,5,4})
        { var entries=catalog.Entries.Where(e=>e.Version==version).ToList();Check(entries.Count==12 && entries.Select(e=>e.NameId).Distinct().Count()==12 && entries.All(e=>!string.IsNullOrWhiteSpace(e.Name))); }
    }
    private static void Empty()
    {
        var roster=Catalog();var filter=Filter(roster);var rows=new List<HuntTrainMob>();var groups=filter.Groups(rows);
        Check(groups.Take(4).Select(g=>g.Version).SequenceEqual(new int?[]{7,6,5,4}));
        Check(groups.Take(4).All(g=>g.Count==0&&g.Expected==12));Check(filter.Selected(101,rows)==7);
        filter.Select(101,4);Check(filter.Selected(101,rows)==4 && roster.Missing(101,4,rows).Count==12);
        Check(filter.VisibleIndices(101,rows).Count==0 && rows.Count==0);
    }
    private static void Discovery()
    {
        var roster=Catalog();var entry=roster.Entries.First(e=>e.Version==7);var rows=new[]{Found(entry)};
        Check(roster.Missing(101,7,rows).Count==11 && roster.Missing(101,7,rows).All(e=>e.NameId!=entry.NameId));
        Check(roster.Missing(101,6,rows).Count==12 && roster.Progress(101,7,rows)==new HuntRosterProgress(1,12));
        Check(Filter(roster).Groups(rows).First().DisplayLabel=="7A (1/12)");
    }
    private static void Worlds()
    {
        var roster=Catalog();var rows=new[]{Found(roster.Entries[0],world:101)};
        Check(roster.Progress(101,7,rows).Found==1 && roster.Progress(102,7,rows).Found==0);
        Check(roster.Missing(102,7,rows).Count==12);
    }
    private static void Instances()
    {
        var roster=Catalog();var entry=roster.Entries[0];var rows=new[]{Found(entry,instance:1),Found(entry,instance:2),Found(entry,instance:3)};
        Check(roster.Progress(101,7,rows).Found==1 && roster.Missing(101,7,rows).Count==11);
        Check(Filter(roster).VisibleIndices(101,rows).Count==3 && Filter(roster).Groups(rows).First().Count==1);
    }
    private static void Delete()
    {
        var roster=Catalog();var rows=new List<HuntTrainMob>{Found(roster.Entries[0]),Found(roster.Entries[1])};
        rows[0].Dead=true;Check(roster.Progress(101,7,rows).Found==2);
        rows.RemoveAt(0);Check(roster.Missing(101,7,rows).Count==11 && roster.Missing(101,7,rows).Any(e=>e.NameId==roster.Entries[0].NameId));
        rows.Clear();Check(Filter(roster).Groups(rows).First().Expected==12 && roster.Missing(101,7,rows).Count==12);
    }
    private static void Sharing()
    {
        var roster=Catalog();var rows=new List<HuntTrainMob>();var before=ExportImport.Export(rows);
        Filter(roster).Groups(rows);roster.Missing(101,7,rows);roster.Progress(101,7,rows);
        Check(ExportImport.Export(rows)==before && rows.Count==0);
        rows.Add(Found(roster.Entries[0]));var indices=Filter(roster).VisibleIndices(101,rows);
        var restored=ExportImport.Import(ExportImport.Export(indices.Select(i=>rows[i]).ToList()),new List<HuntTrainMob>());
        Check(restored.Count==1 && restored[0].MobID==rows[0].MobID && restored[0].WorldPosition==rows[0].WorldPosition);
    }
}
