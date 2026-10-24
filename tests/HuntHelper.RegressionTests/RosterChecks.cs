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
        ("roster display cannot fabricate exportable records or navigation positions", Sharing)
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
