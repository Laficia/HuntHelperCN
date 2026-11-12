using HuntHelper.Utilities;
using Newtonsoft.Json;

internal static class MacroLibraryChecks
{
    public static readonly (string Name, Action Run)[] Tests =
    [
        ("Existing A/S macros migrate to the collection without changing their text", Existing),
        ("Adding and renaming custom pages preserves stable collection identities", Custom),
        ("Collection supports before after append and unchanged drops without duplicates", Reorder),
        ("Unpinning preserves macro content while deleting a custom macro removes its button", Removal),
        ("Collection order names templates and independent A/S fields survive serialization", Persistence),
        ("Malformed collection entries repair safely without replacing an intentionally empty collection", Repair),
        ("Plain custom reminders can send without selecting a monster", PlainMacro),
        ("Record-dependent macros require a selected monster and never fabricate a target", MissingTarget),
    ];
    private static void Check(bool ok) { if (!ok) throw new Exception("Macro collection assertion failed"); }
    private static void Existing()
    {
        var settings = JsonConvert.DeserializeObject<HuntPointMacroSettings>("{\"A\":\"/p old A<flag>\",\"S\":\"/sh old S<flag>\"}")!;
        var lib = new HuntMacroLibrary(settings); lib.Repair();
        Check(lib.Collection.SequenceEqual(new[] { "a", "s" }) && settings.A == "/p old A<flag>" && settings.S == "/sh old S<flag>");
    }
    private static void Custom()
    {
        var settings = new HuntPointMacroSettings(); var lib = new HuntMacroLibrary(settings); var page = lib.Add();
        Check(!page.BuiltIn && page.Id is not ("a" or "s") && !lib.Collection.Contains(page.Id));
        Check(lib.Pin(page.Id) && lib.Save(page.Id, "倒计时提醒", "/sh 请等待倒计时"));
        Check(lib.Collection.Last() == page.Id && lib.Find(page.Id)!.Name == "倒计时提醒" && settings.A == HuntPointMacroSettings.DefaultTemplate);
    }
    private static void Reorder()
    {
        var lib = new HuntMacroLibrary(new()); var custom = lib.Add(); lib.Pin(custom.Id);
        Check(lib.Pin(custom.Id, "a", false) && lib.Collection.SequenceEqual(new[] { custom.Id, "a", "s" }));
        Check(lib.Pin(custom.Id, "s", true) && lib.Collection.SequenceEqual(new[] { "a", "s", custom.Id }));
        Check(!lib.Pin(custom.Id) && !lib.Pin("a", "a") && !lib.Pin("missing") && !lib.Pin("a", "missing"));
        Check(lib.Pin("a") && lib.Collection.SequenceEqual(new[] { "s", custom.Id, "a" }));
    }
    private static void Removal()
    {
        var lib = new HuntMacroLibrary(new()); var custom = lib.Add(); lib.Pin(custom.Id);
        Check(lib.Unpin(custom.Id) && lib.Find(custom.Id) != null);
        lib.Pin(custom.Id); Check(lib.Delete(custom.Id) && lib.Find(custom.Id) == null && !lib.Collection.Contains(custom.Id));
        Check(!lib.Delete("a") && !lib.Delete("s") && lib.Entries.Count == 2);
    }
    private static void Persistence()
    {
        var settings = new HuntPointMacroSettings(); var lib = new HuntMacroLibrary(settings); var page = lib.Add();
        lib.Save("a", "队长A", "/p A<name><flag>"); lib.Save("s", "S喊话", "/sh <sever><name><flag>");
        lib.Save(page.Id, "集合", "/sh <map>集合<flag>"); lib.Pin(page.Id, "a", false);
        var restored = JsonConvert.DeserializeObject<HuntPointMacroSettings>(JsonConvert.SerializeObject(settings))!;
        var copy = new HuntMacroLibrary(restored); Check(!copy.Repair());
        Check(copy.Collection.SequenceEqual(lib.Collection) && copy.Entries.SequenceEqual(lib.Entries));
    }
    private static void Repair()
    {
        var settings = new HuntPointMacroSettings { Collection = new() { "a", "a", "missing" } };
        settings.Custom.Add(new() { Id = "a", Name = "重复ID", Text = "/sh reminder" });
        var lib = new HuntMacroLibrary(settings); Check(lib.Repair());
        Check(lib.Collection.SequenceEqual(new[] { "a" }) && lib.Entries.Select(m => m.Id).Distinct().Count() == 3);
        settings.Collection.Clear(); Check(!lib.Repair() && lib.Collection.Count == 0 && settings.Custom.Single().Text == "/sh reminder");
    }
    private static void PlainMacro()
    {
        var lines = HuntPointChatDelivery.Build(HuntPointMacro.Expand("/sh 请等待倒计时，不要抢开！", null, "A", 101));
        Check(lines.Single().Channel == "/sh" && !lines.Single().UsesFlag);
    }
    private static void MissingTarget()
    {
        foreach (var variable in new[] { "<flag>", "<name>", "<sever>", "<map>", "<lv>", "<ins>" })
        {
            var failed = false;
            try { HuntPointMacro.Expand("/sh " + variable, null, "S", 101); }
            catch (ArgumentException error) { failed = error.Message.Contains("选中怪物"); }
            Check(failed);
        }
    }
}
