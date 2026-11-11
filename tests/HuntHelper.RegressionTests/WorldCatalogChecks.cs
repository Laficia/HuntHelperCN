using HuntHelper.Utilities;
using System.Text.Json;

internal static class WorldCatalogChecks
{
    public static readonly (string Name,Action Run)[] Tests =
    [
        ("actual CN sheet fixture yields four data centers and twenty-nine non-public live worlds", China),
        ("merged duplicate names and low-ID staging worlds cannot enter the CN selector", Retired),
        ("CN catalog works during partial current-world metadata without adding global servers", Partial),
        ("international catalogs retain the public-flag and region boundaries", Global),
        ("unknown international region and invalid world rows cannot widen a catalog", Invalid),
    ];
    private static List<HuntWorldCatalogEntry> Rows()=>JsonSerializer.Deserialize<List<HuntWorldCatalogEntry>>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"CnWorldCatalog.json")))!;
    private static void Check(bool ok){if(!ok)throw new Exception("World catalog assertion failed");}
    private static void China()
    {
        var rows=Rows();var worlds=HuntWorldCatalog.Select(rows,1183,true);
        Check(worlds.Count==29 && worlds.Select(w=>w.DataCenterName).Distinct().SequenceEqual(new[]{"陆行鸟","莫古力","猫小胖","豆豆柴"}));
        Check(worlds.Count(w=>w.DataCenterName=="豆豆柴")==6 && worlds.All(w=>!rows.Single(r=>r.WorldId==w.WorldID).IsPublic));
    }
    private static void Retired()
    {
        var worlds=HuntWorldCatalog.Select(Rows(),1183,true);
        Check(worlds.Single(w=>w.WorldName=="银泪湖").WorldID==1183 && worlds.Single(w=>w.WorldName=="红茶川").WorldID==1201);
        Check(worlds.All(w=>w.WorldID>=1000) && worlds.All(w=>w.WorldID is not (1050 or 1056)));
    }
    private static void Partial()
    {Check(HuntWorldCatalog.Select(Rows(),0,true).Count==29 && HuntWorldCatalog.Select(Rows(),1183,false).Count==29);}
    private static void Global()
    {
        HuntWorldCatalogEntry[] rows=[new(1,"EU",2,"Europe",2,true),new(2,"JP",1,"Japan",1,true),new(3,"Hidden",2,"Europe",2,false),new(1183,"银泪湖",104,"豆豆柴",5,false)];
        Check(HuntWorldCatalog.Select(rows,1,false).Single().WorldID==1);
    }
    private static void Invalid()
    {
        HuntWorldCatalogEntry[] rows=[new(1,"EU",2,"Europe",2,true),new(0,"invalid",2,"Europe",2,true),new(3,"",2,"Europe",2,true),new(4,"MissingDC",0,"",2,true)];
        Check(HuntWorldCatalog.Select(rows,999,false).Count==0 && HuntWorldCatalog.Select(rows,1,false).Single().WorldID==1);
    }
}
