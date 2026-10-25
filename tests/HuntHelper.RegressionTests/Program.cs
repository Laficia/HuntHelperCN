using System.Numerics;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Plugin.Services;
using HuntHelper.Managers;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using HuntHelper.Utilities;
using Newtonsoft.Json;
using HuntHelper;

internal static class Program
{
    private static readonly HuntTrainWorld WorldA = new(101, "A服", "测试大区", 0);
    private static readonly HuntTrainWorld WorldB = new(102, "B服", "测试大区", 0);
    private static readonly DateTime Seen = new(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);

    private static async Task<int> Main()
    {
        (string Name, Func<Task> Run)[] tests =
        [
            .. RecoveryChecks.Tests.Select(t => (t.Name, Sync(t.Run))),
            .. RelayChecks.Tests.Select(t => (t.Name, Sync(t.Run))),
            .. AutomationChecks.Tests.Select(t => (t.Name, Sync(t.Run))),
            .. VersionTabChecks.Tests.Select(t => (t.Name, Sync(t.Run))),
            .. RosterChecks.Tests.Select(t => (t.Name, Sync(t.Run))),
            ("Chinese resources cover every shipped English key with nonempty localized text", Sync(LocalizationCoverage)),
            ("same mob/instance coexists on different worlds", Sync(WorldIdentity)),
            ("browsing B keeps recording and scans in visited A", Sync(RecordingWhileBrowsing)),
            ("delete/dead/reorder changes stay in their world", Sync(WorldScopedMutations)),
            ("import replacement respects explicit world boundaries", Sync(ScopedImportReplacement)),
            ("legacy import captures destination before tab changes", Sync(CapturedLegacyImport)),
            ("legacy single-world code can target a new server without creating an empty tab", Sync(LegacyImportDestination)),
            ("single-world destination changes preserve every hunt field and source code", Sync(ExplicitImportDestination)),
            ("empty and multi-world codes cannot be flattened into one server", Sync(MultipleImportDestinations)),
            ("replacement after choosing a server leaves every other server intact", Sync(SelectedImportReplacement)),
            ("merge after choosing a server deduplicates and updates only the destination", Sync(SelectedImportMerge)),
            ("newer 2D import clears stale world position", Sync(NewerTwoDimensionalImport)),
            ("all worlds and record order survive save/reload", Sync(PersistenceRoundTrip)),
            ("legacy local records stay in unknown world 0", Sync(LegacyLocalFile)),
            ("legacy sharing inherits destination; explicit world 0 stays 0", Sync(SharingCompatibility)),
            ("corrupt persisted files cannot be overwritten", Sync(CorruptPersistence)),
            ("invalid sharing leaves the supplied fallback intact", Sync(InvalidSharing)),
            ("navigation requires same world/map/instance", Sync(NavigationLocations)),
            ("navigation coordinate inverse and invalid inputs", Sync(NavigationCoordinates)),
            ("legacy invisible map settings repair once without erasing preferences", Sync(MapVisibilityMigration)),
            ("map cache survives upgrades without replacing valid current images", Sync(MapCacheMigration)),
            ("server tab drag/menu order survives configuration reload", Sync(ServerTabOrder)),
            ("stationary A rank is rediscovered after deletion and recording restart", Sync(StationaryRecording)),
            ("empty server tabs disappear and deletion stays world scoped", Sync(EmptyServerTabs)),
            ("single-server and all-server share codes preserve export scope and world isolation", Sync(ExportScopes)),
            ("downloads reuse cache and atomically save successes", DownloadCache),
            ("failed refreshes preserve existing map images", DownloadFailure),
            ("cancellation preserves cache and removes temporary files", DownloadCancellation),
            ("download concurrency is bounded at four", DownloadConcurrency),
        ];

        var failures = 0;
        foreach (var test in tests)
        {
            try { await test.Run(); Console.WriteLine($"PASS {test.Name}"); }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}\n{error}");
            }
        }
        Console.WriteLine($"{tests.Length - failures}/{tests.Length} regression groups passed.");
        return failures == 0 ? 0 : 1;
    }

    private static void LocalizationCoverage()
    {
        var folder=Path.Combine("HuntHelper","Data","Localisation");
        var chinese=JsonConvert.DeserializeObject<Dictionary<string,Dictionary<string,string>>>(File.ReadAllText(Path.Combine(folder,"simplified chinese.json")))!;
        var english=JsonConvert.DeserializeObject<Dictionary<string,Dictionary<string,string>>>(File.ReadAllText(Path.Combine(folder,"english.json")))!;
        foreach(var group in english)
            foreach(var entry in group.Value)
                if(!chinese.TryGetValue(group.Key,out var translated) || !translated.TryGetValue(entry.Key,out var value) ||
                    string.IsNullOrWhiteSpace(value) || !value.Any(c=>c is >= '\u4e00' and <= '\u9fff'))
                    throw new Exception($"Missing Chinese resource {group.Key}/{entry.Key}");
    }

    private static Func<Task> Sync(Action action) => () => { action(); return Task.CompletedTask; };
    private static HuntTrainMob Mob(uint id = 1, uint instance = 1, uint? world = 101,
        DateTime? seen = null, Vector2? position = null, Vector3? worldPosition = null) =>
        new($"Mob {id}", id, 134, 30, instance, "Middle La Noscea", position ?? new Vector2(23.48f, 17.48f),
            seen ?? Seen, false, world, world == 102 ? "B服" : "A服", "测试大区", worldPosition);

    private static void WorldIdentity()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob());
        f.Manager.AddMob(Mob());
        f.Manager.AddMob(Mob(instance: 2));
        f.VisitedWorld = WorldB;
        f.Manager.AddMob(Mob());
        Equal(102u, f.Manager.SelectedWorldId);
        Equal(2, f.Rows(101).Count);
        Equal(1, f.Rows(102).Count);
        True(f.Rows(101).All(m => m.WorldID == 101 && m.WorldName == "A服"));
        True(f.Rows(102).All(m => m.WorldID == 102 && m.WorldName == "B服"));
        Equal(2, f.Manager.GetWorlds().Single(w => w.WorldID == 101).Count);
    }

    private static void RecordingWhileBrowsing()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob());
        f.VisitedWorld = WorldB;
        f.Manager.AddMob(Mob());
        f.Manager.SelectWorld(102);
        f.VisitedWorld = WorldA;
        var scanned = new BattleNpc(1, new Vector3(100, 20, -200));
        True(f.Manager.UpdateLastSeen(scanned, 1, 100));
        f.Manager.AddMob(Mob(2));
        Equal(102u, f.Manager.SelectedWorldId);
        Equal(1, f.Manager.HuntTrain.Count);
        Equal(Seen, f.Manager.HuntTrain[0].LastSeenUTC);
        var a = f.Rows(101);
        Equal(2, a.Count);
        Equal(scanned.Position, a[0].WorldPosition);
        Near(23.48f, a[0].Position.X);
        Near(17.48f, a[0].Position.Y);
        True(a[0].LastSeenUTC > Seen);
        True(!f.Manager.UpdateLastSeen(new BattleNpc(999, Vector3.Zero), 1));
    }

    private static void WorldScopedMutations()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob(1)); f.Manager.AddMob(Mob(2));
        f.VisitedWorld = WorldB;
        f.Manager.AddMob(Mob(3)); f.Manager.AddMob(Mob(4));
        f.Rows(101); f.Manager.SetDead(0, true);
        var untouchedA = JsonConvert.SerializeObject(f.Manager.HuntTrain);
        f.Rows(102); f.Manager.TrainMarkAsDead(1); f.Manager.SwapTrainMobs(0, 1);
        Sequence(new uint[] { 4, 3 }, f.Manager.HuntTrain.Select(m => m.MobID));
        f.Manager.TrainRemoveDead();
        Equal(3u, f.Manager.HuntTrain.Single().MobID);
        f.Manager.SetDead(0, true); f.Manager.TrainUnkillAll();
        True(!f.Manager.HuntTrain[0].Dead);
        f.Manager.TrainRemove(f.Manager.HuntTrain[0]);
        Equal(0, f.Manager.HuntTrain.Count);
        f.Manager.AddMob(Mob(5)); f.Manager.TrainDelete();
        Equal(0, f.Manager.HuntTrain.Count);
        Equal(untouchedA, JsonConvert.SerializeObject(f.Rows(101)));
    }

    private static void ScopedImportReplacement()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob(1));
        f.VisitedWorld = WorldB; f.Manager.AddMob(Mob(2));
        var untouchedB = JsonConvert.SerializeObject(f.Rows(102));
        f.Manager.Import(new[] { Mob(9, world: 101) });
        f.Manager.ImportTrainAll();
        Equal(9u, f.Rows(101).Single().MobID);
        Equal(untouchedB, JsonConvert.SerializeObject(f.Rows(102)));
        f.Manager.Import(new[] { Mob(11, world: 101), Mob(12, world: 102), Mob(13, world: 0) });
        f.Manager.ImportTrainAll();
        Equal(11u, f.Rows(101).Single().MobID);
        Equal(12u, f.Rows(102).Single().MobID);
        Equal(13u, f.Rows(0).Single().MobID);
    }

    private static void CapturedLegacyImport()
    {
        using var f = new TrainFixture();
        f.Manager.SelectWorld(101);
        f.Manager.Import(new[] { Mob(8, world: null) });
        f.Manager.SelectWorld(102);
        f.Manager.ImportTrainAll();
        Equal(101u, f.Rows(101).Single().WorldID);
        Equal(0, f.Rows(102).Count);
        f.Manager.Import(new[] { Mob(10, world: null) }, legacyWorldId: 101);
        f.Manager.ImportTrainAll();
        Equal(10u, f.Rows(101).Single().MobID);
        Equal(0, f.Rows(102).Count);
    }

    private static void LegacyImportDestination()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob(1));
        var saved = File.ReadAllText(f.FilePath);
        f.Manager.Import(ExportImport.Export(LegacyRows()));
        True(f.Manager.CanChooseImportWorld);
        var target = new HuntTrainWorld(103, "C服", "其他大区", 0);
        True(f.Manager.SetImportWorld(target));
        Equal(103u, f.Manager.ImportedTrain.Single().WorldID);
        Equal("C服", f.Manager.ImportedTrain.Single().WorldName);
        Equal(saved, File.ReadAllText(f.FilePath));
        True(!f.Manager.GetWorlds().Any(w => w.WorldID == 103));
        True(f.Manager.GetImportWorlds().Any(w => w.WorldID == 103));
        f.Manager.ImportedTrain.Clear(); // Cancel the preview.
        Equal(saved, File.ReadAllText(f.FilePath));
        True(!f.Manager.GetWorlds().Any(w => w.WorldID == 103));
        f.Manager.Import(ExportImport.Export(LegacyRows()));
        True(f.Manager.SetImportWorld(target));
        f.Manager.ImportTrainNew(true);
        Equal(77u, f.Rows(103).Single().MobID);
        Equal(1u, f.Rows(101).Single().MobID);
        using var reload = new TrainManager(NullHost.Instance, NullHost.Instance, f.FilePath, () => WorldA);
        reload.SelectWorld(103);
        Equal("C服", reload.HuntTrain.Single().WorldName);
    }

    private static void ExplicitImportDestination()
    {
        using var f = new TrainFixture();
        var source = Mob(7, instance: 2, worldPosition: new Vector3(40, 50, 60));
        source.Dead = true;
        var original = JsonConvert.SerializeObject(source);
        f.Manager.Import(new[] { source });
        True(f.Manager.SetImportWorld(WorldB));
        var preview = f.Manager.ImportedTrain.Single();
        Equal(102u, preview.WorldID);
        Equal("B服", preview.WorldName);
        Equal(WorldB.DataCenterName, preview.DataCenterName);
        Equal(source.Name, preview.Name);
        Equal(source.MobID, preview.MobID);
        Equal(source.Instance, preview.Instance);
        Equal(source.TerritoryID, preview.TerritoryID);
        Equal(source.MapID, preview.MapID);
        Equal(source.MapName, preview.MapName);
        Equal(source.Position, preview.Position);
        Equal(source.WorldPosition, preview.WorldPosition);
        Equal(source.LastSeenUTC, preview.LastSeenUTC);
        Equal(source.Dead, preview.Dead);
        Equal(original, JsonConvert.SerializeObject(source));
        True(f.Manager.SetImportWorld(WorldA));
        var imported = f.Manager.ImportedTrain.Single();
        Equal(HuntRecordSource.Import, imported.Source);
        True(imported.ImportedAtUtc.HasValue);
        Equal(source.LastSeenUTC, imported.LastSeenUTC);
        Equal(source.WorldPosition, imported.WorldPosition);
        Equal(original, JsonConvert.SerializeObject(source));
        f.Manager.SelectWorld(102);
        f.Manager.ImportTrainAll();
        Equal(7u, f.Rows(101).Single().MobID);
        Equal(0, f.Rows(102).Count);
    }

    private static void MultipleImportDestinations()
    {
        using var f = new TrainFixture();
        True(!f.Manager.CanChooseImportWorld);
        True(!f.Manager.SetImportWorld(WorldA));
        f.Manager.Import(new[] { Mob(1, world: 101), Mob(2, world: 102), Mob(3, world: 0) });
        var original = JsonConvert.SerializeObject(f.Manager.ImportedTrain);
        True(!f.Manager.CanChooseImportWorld);
        True(!f.Manager.SetImportWorld(WorldB));
        Equal(original, JsonConvert.SerializeObject(f.Manager.ImportedTrain));
        f.Manager.ImportTrainAll();
        Equal(1u, f.Rows(101).Single().MobID);
        Equal(2u, f.Rows(102).Single().MobID);
        Equal(3u, f.Rows(0).Single().MobID);
    }

    private static void SelectedImportReplacement()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob(1));
        f.VisitedWorld = WorldB;
        f.Manager.AddMob(Mob(2));
        var untouched = JsonConvert.SerializeObject(f.Rows(101));
        f.Manager.Import(new[] { Mob(9), Mob(10) });
        True(f.Manager.SetImportWorld(WorldB));
        f.Manager.SelectWorld(101);
        f.Manager.ImportTrainAll();
        Equal(untouched, JsonConvert.SerializeObject(f.Rows(101)));
        Sequence(new uint[] { 9, 10 }, f.Rows(102).Select(m => m.MobID));
        True(f.Rows(102).All(m => m.WorldName == "B服"));
    }

    private static void SelectedImportMerge()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob(1));
        f.VisitedWorld = WorldB;
        f.Manager.AddMob(Mob(1));
        var untouched = JsonConvert.SerializeObject(f.Rows(101));
        f.Manager.Import(new[] { Mob(1, seen: Seen.AddMinutes(1)), Mob(2), Mob(2) });
        True(f.Manager.SetImportWorld(WorldB));
        True(f.Manager.ContainsMob(f.Manager.ImportedTrain[0]));
        True(!f.Manager.ContainsMob(f.Manager.ImportedTrain[1]));
        f.Manager.ImportTrainNew(true);
        Equal(untouched, JsonConvert.SerializeObject(f.Rows(101)));
        Sequence(new uint[] { 1, 2 }, f.Rows(102).Select(m => m.MobID));
        Equal(Seen.AddMinutes(1), f.Rows(102)[0].LastSeenUTC);
        True(f.Rows(102).All(m => m.WorldID == 102));
    }

    private static void NewerTwoDimensionalImport()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob(worldPosition: new Vector3(100, 20, -200)));
        f.Manager.Import(new[] { Mob(seen: Seen.AddMinutes(1), position: new Vector2(30, 31)) });
        f.Manager.ImportTrainNew(true);
        var updated = f.Rows(101).Single();
        Equal(Seen.AddMinutes(1), updated.LastSeenUTC);
        Equal(new Vector2(30, 31), updated.Position);
        True(updated.WorldPosition == null, "A newer 2D record must discard the stale 3D destination.");
        f.Manager.Import(new[] { Mob(seen: Seen.AddMinutes(-1), position: Vector2.Zero, worldPosition: Vector3.Zero) });
        f.Manager.ImportTrainNew(true);
        Equal(new Vector2(30, 31), f.Rows(101).Single().Position);
        True(f.Manager.HuntTrain[0].WorldPosition == null);
        f.Manager.Import(new[] { Mob(seen: Seen.AddMinutes(2), position: Vector2.One) });
        f.Manager.ImportTrainNew(false);
        Equal(new Vector2(30, 31), f.Rows(101).Single().Position);
        f.Manager.Import(new[] { Mob(world: 102), Mob(instance: 2) });
        f.Manager.ImportTrainNew(true);
        Equal(2, f.Rows(101).Count);
        Equal(1, f.Rows(102).Count);
    }

    private static void PersistenceRoundTrip()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob(1, worldPosition: new Vector3(10, 20, 30)));
        f.Manager.AddMob(Mob(2)); f.Manager.SwapTrainMobs(0, 1); f.Manager.SetDead(0, true);
        f.VisitedWorld = WorldB; f.Manager.AddMob(Mob(3));
        f.Manager.Import(new[] { Mob(4, world: 0) }); f.Manager.ImportTrainAll();
        f.Rows(101); f.Manager.SaveHuntTrainRecord();
        using var reloaded = new TrainManager(NullHost.Instance, NullHost.Instance, f.FilePath, () => WorldA);
        reloaded.SelectWorld(101);
        Sequence(new uint[] { 2, 1 }, reloaded.HuntTrain.Select(m => m.MobID));
        True(reloaded.HuntTrain[0].Dead);
        Equal(new Vector3(10, 20, 30), reloaded.HuntTrain[1].WorldPosition);
        reloaded.SelectWorld(102); Equal(3u, reloaded.HuntTrain.Single().MobID);
        reloaded.SelectWorld(0); Equal(4u, reloaded.HuntTrain.Single().MobID);
        Equal(3, reloaded.GetWorlds().Count);
        True(!Directory.EnumerateFiles(f.Directory.Path, "*.tmp").Any());
    }

    private static object[] LegacyRows() =>
    [
        new { Name = "Legacy mob", MobID = 77u, TerritoryID = 134u, MapID = 30u, Instance = 1u,
            MapName = "Middle La Noscea", Position = new Vector2(20, 21), LastSeenUTC = Seen, Dead = false }
    ];

    private static void LegacyLocalFile()
    {
        using var f = new TrainFixture(JsonConvert.SerializeObject(LegacyRows()));
        Equal(0, f.Rows(101).Count);
        var legacy = f.Rows(0).Single();
        Equal(0u, legacy.WorldID);
        True(!legacy.HasWorldMetadata);
        Equal("未标注区服", legacy.WorldName);
        f.Manager.SaveHuntTrainRecord();
        using var reloaded = new TrainManager(NullHost.Instance, NullHost.Instance, f.FilePath, () => WorldB);
        Equal(0, reloaded.HuntTrain.Count);
        reloaded.SelectWorld(0); Equal(77u, reloaded.HuntTrain.Single().MobID);
    }

    private static void SharingCompatibility()
    {
        using var f = new TrainFixture();
        var oldCode = ExportImport.Export(LegacyRows());
        var oldRows = ExportImport.Import(oldCode, new List<HuntTrainMob>());
        True(!oldRows.Single().HasWorldMetadata);
        f.Manager.SelectWorld(102); f.Manager.Import(oldCode); f.Manager.ImportTrainAll();
        Equal(102u, f.Rows(102).Single().WorldID);
        var newCode = ExportImport.Export(new[] { Mob(88, world: 0) });
        var newRows = ExportImport.Import(newCode, new List<HuntTrainMob>());
        True(newRows.Single().HasWorldMetadata);
        Equal(0u, newRows.Single().WorldID);
        f.Manager.SelectWorld(102); f.Manager.Import(newCode); f.Manager.ImportTrainAll();
        Equal(88u, f.Rows(0).Single().MobID);
        Equal(77u, f.Rows(102).Single().MobID);
    }

    private static void CorruptPersistence()
    {
        foreach (var invalid in new[] { "{ broken", "null", "{}" })
        {
            using var f = new TrainFixture(invalid);
            True(f.Manager.PersistenceError != null);
            f.Manager.AddMob(Mob()); f.Manager.TrainDelete();
            f.Manager.SaveHuntTrainRecord(); f.Manager.Dispose();
            Equal(invalid, File.ReadAllText(f.FilePath));
        }
    }

    private static void InvalidSharing()
    {
        var fallback = new List<HuntTrainMob> { Mob() };
        True(ReferenceEquals(fallback, ExportImport.Import("not a sharing code", fallback)));
        True(ReferenceEquals(fallback, ExportImport.Import(ExportImport.Export("not a list"), fallback)));
        Equal(1, fallback.Count);
    }

    private static void NavigationLocations()
    {
        Equal(string.Empty, NavigationRules.GetLocationMismatch(101, 134, 30, 1, 101, 134, 30, 1));
        True(NavigationRules.GetLocationMismatch(0, 134, 30, 1, 101, 134, 30, 1).Length > 0);
        True(NavigationRules.GetLocationMismatch(102, 134, 30, 1, 101, 134, 30, 1).Length > 0);
        True(NavigationRules.GetLocationMismatch(101, 135, 30, 1, 101, 134, 30, 1).Length > 0);
        True(NavigationRules.GetLocationMismatch(101, 134, 31, 1, 101, 134, 30, 1).Length > 0);
        True(NavigationRules.GetLocationMismatch(101, 134, 30, 2, 101, 134, 30, 1).Length > 0);
        Equal(string.Empty, NavigationRules.GetLocationMismatch(101, 134, 30, 0, 101, 134, 30, 0));
    }

    private static void NavigationCoordinates()
    {
        var position = NavigationRules.FromHuntMapCoordinates(new Vector2(23.48f, 17.48f), 100);
        Near(100, position.X); Near(-200, position.Z); Near(1024, position.Y);
        var heavensward = NavigationRules.FromHuntMapCoordinates(new Vector2(2048f / 95 + 3, 2048f / 95 - 3), 95);
        Near(100, heavensward.X); Near(-200, heavensward.Z);
        foreach (var factor in new[] { 0, -1, float.NaN, float.PositiveInfinity })
            Throws<ArgumentOutOfRangeException>(() => NavigationRules.FromHuntMapCoordinates(Vector2.One, factor));
        Throws<ArgumentOutOfRangeException>(() => NavigationRules.FromHuntMapCoordinates(new Vector2(float.NaN, 1), 100));
        Throws<ArgumentOutOfRangeException>(() => NavigationRules.FromHuntMapCoordinates(new Vector2(1, float.NegativeInfinity), 100));
        True(NavigationRules.IsFinite(position));
        True(!NavigationRules.IsFinite(new Vector3(1, float.NaN, 3)));
    }

    // Transport fixtures satisfy the downloader's JPEG marker check; image decoding is outside this harness.
    private static readonly byte[] Jpeg = [0xff, 0xd8, 0xff, 0xe0, 0, 2, 0xff, 0xd9];

    private static void MapVisibilityMigration()
    {
        var config = JsonConvert.DeserializeObject<Configuration>("""
            {"MapImageOpacityAsPercentage":1,"UseMapImages":true,
             "PresetOne":{"MapOpacity":1,"UseMap":false,"WindowSize":777},
             "PresetTwo":{"MapOpacity":60,"UseMap":true},"HuntTrainWorldTabOrder":[102,101]}
            """)!;
        True(config.RepairLegacyMapImageVisibility());
        Equal(100f, config.MapImageOpacityAsPercentage);
        Equal(100f, config.PresetOne.MapOpacity);
        Equal(60f, config.PresetTwo.MapOpacity);
        Equal(777, config.PresetOne.WindowSize);
        True(!config.PresetOne.UseMap);
        Sequence(new uint[] { 102, 101 }, config.HuntTrainWorldTabOrder);
        config.MapImageOpacityAsPercentage = 0; // A later deliberate choice must remain valid.
        var reloaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        True(!reloaded.RepairLegacyMapImageVisibility());
        Equal(0f, reloaded.MapImageOpacityAsPercentage);
        var custom = new Configuration { MapImageOpacityAsPercentage = 55 };
        custom.RepairLegacyMapImageVisibility();
        Equal(55f, custom.MapImageOpacityAsPercentage);
        Equal(100f, new Configuration().PresetOne.MapOpacity);
    }

    private static void MapCacheMigration()
    {
        using var oldCache = new TemporaryDirectory();
        using var persistentCache = new TemporaryDirectory();
        var newer = Jpeg.Concat(new byte[] { 0xff, 0xd9 }).ToArray();
        File.WriteAllBytes(Path.Combine(oldCache.Path, "fresh-data.jpg"), Jpeg);
        File.WriteAllBytes(Path.Combine(oldCache.Path, "keep-data.jpg"), Jpeg);
        File.WriteAllBytes(Path.Combine(oldCache.Path, "broken-data.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(persistentCache.Path, "keep-data.jpg"), newer);
        Equal(1, ImageDownloader.ImportCachedImages(oldCache.Path, persistentCache.Path));
        Sequence(Jpeg, File.ReadAllBytes(Path.Combine(persistentCache.Path, "fresh-data.jpg")));
        Sequence(newer, File.ReadAllBytes(Path.Combine(persistentCache.Path, "keep-data.jpg")));
        True(!File.Exists(Path.Combine(persistentCache.Path, "broken-data.jpg")));
        Equal(0, ImageDownloader.ImportCachedImages(oldCache.Path, persistentCache.Path));
        Sequence(Jpeg, File.ReadAllBytes(Path.Combine(oldCache.Path, "fresh-data.jpg")));
    }

    private static void ServerTabOrder()
    {
        var order = new List<uint> { 101, 102, 103 };
        True(WorldTabOrder.MoveRelative(order, 101, 103, true));
        Sequence(new uint[] { 102, 103, 101 }, order);
        True(WorldTabOrder.MoveRelative(order, 101, 102, false));
        Sequence(new uint[] { 101, 102, 103 }, order);
        True(WorldTabOrder.MoveBy(order, 102, -1));
        Sequence(new uint[] { 102, 101, 103 }, order);
        True(!WorldTabOrder.MoveBy(order, 102, -1));
        True(!WorldTabOrder.MoveRelative(order, 101, 101, true));
        var config = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(new Configuration { HuntTrainWorldTabOrder = order }))!;
        True(WorldTabOrder.Synchronize(config.HuntTrainWorldTabOrder, new uint[] { 101, 103, 104 }));
        Sequence(new uint[] { 101, 103, 104 }, config.HuntTrainWorldTabOrder);
        True(!WorldTabOrder.Synchronize(config.HuntTrainWorldTabOrder, new uint[] { 104, 103, 101 }));
    }

    private static void StationaryRecording()
    {
        using var f = new TrainFixture();
        var npc = new BattleNpc(1, new Vector3(100, 20, -200));
        void Scan(bool isARank = true) => f.Manager.ObserveMob(npc, isARank, 134, 30, 1, "Middle La Noscea", 100);
        f.Manager.RecordTrain = true;
        Scan(); Scan();
        Equal(1, f.Rows(101).Count);
        f.Manager.TrainRemove(f.Manager.HuntTrain[0]);
        Equal(0, f.Manager.GetWorlds().Count);
        Scan(); // Same object, same place, no zone change or arrival notification.
        Equal(1, f.Rows(101).Count);
        f.Manager.TrainDelete();
        True(!f.Manager.RecordTrain);
        Scan();
        Equal(0, f.Manager.GetWorlds().Count);
        f.Manager.RecordTrain = true;
        Scan(false); Equal(0, f.Manager.GetWorlds().Count);
        Scan(); Equal(1, f.Rows(101).Count);
        Equal(npc.Position, f.Manager.HuntTrain[0].WorldPosition);
        f.Manager.SelectWorld(102); Scan();
        Equal(0, f.Manager.HuntTrain.Count);
        Equal(1, f.Rows(101).Count);
    }

    private static void EmptyServerTabs()
    {
        using var f = new TrainFixture();
        Equal(0, f.Manager.GetWorlds().Count); // Empty current server has no tab either.
        f.Manager.AddMob(Mob());
        f.VisitedWorld = WorldB; f.Manager.AddMob(Mob(2));
        f.Rows(101); f.Manager.SetDead(0, true); f.Manager.TrainRemoveDead();
        Sequence(new uint[] { 102 }, f.Manager.GetWorlds().Select(w => w.WorldID));
        f.Manager.RecordTrain = true;
        f.Manager.DeleteWorld(101); // Deleting a different server must not stop scanning B.
        True(f.Manager.RecordTrain);
        f.Manager.DeleteWorld(102);
        True(!f.Manager.RecordTrain);
        Equal(0, f.Manager.GetWorlds().Count);
        f.Manager.SaveHuntTrainRecord();
        f.Manager.LoadHuntTrainRecord();
        Equal(0, f.Manager.GetWorlds().Count);
    }

    private static void ExportScopes()
    {
        using var f = new TrainFixture();
        f.Manager.AddMob(Mob(worldPosition: new Vector3(10, 20, 30)));
        f.VisitedWorld = WorldB;
        f.Manager.AddMob(Mob(position: new Vector2(30, 31)));
        f.Manager.AddMob(Mob(2));
        f.Manager.SelectWorld(102);
        var selected = ExportImport.Import(f.Manager.ExportSelectedTrain(), new List<HuntTrainMob>());
        Equal(2, selected.Count);
        True(selected.All(m => m.WorldID == 102));
        var allCode = f.Manager.ExportAllTrains(new uint[] { 102, 101 });
        var all = ExportImport.Import(allCode, new List<HuntTrainMob>());
        Sequence(new uint[] { 102, 102, 101 }, all.Select(m => m.WorldID));
        Equal(new Vector3(10, 20, 30), all[2].WorldPosition!.Value);
        Equal(102u, f.Manager.SelectedWorldId);
        using var receiver = new TrainFixture();
        receiver.Manager.Import(allCode); receiver.Manager.ImportTrainAll();
        Equal(1, receiver.Rows(101).Count);
        Equal(2, receiver.Rows(102).Count);
        Equal(1u, receiver.Rows(101)[0].MobID);
        Equal(1u, receiver.Rows(102)[0].MobID); // Same A rank on different servers must coexist.
    }

    private static async Task DownloadCache()
    {
        using var directory = new TemporaryDirectory();
        await using var server = new LoopbackServer();
        File.WriteAllBytes(System.IO.Path.Combine(directory.Path, "cached.jpg"), Jpeg);
        server.Responses["/fresh.jpg"] = new(200, Jpeg);
        var cached = new ImageDownloader([server.Url("cached.jpg")], directory.Path);
        Equal(0, (await cached.BeginDownloadAsync()).Count);
        Equal(0, server.RequestCount("/cached.jpg"));
        var url = server.Url("fresh.jpg");
        var fresh = new ImageDownloader([url, url], directory.Path);
        Equal(0, (await fresh.BeginDownloadAsync()).Count);
        Equal(1, fresh.TotalDownloads); Equal(1, fresh.CompletedDownloads);
        Equal(1, server.RequestCount("/fresh.jpg"));
        Sequence(Jpeg, File.ReadAllBytes(System.IO.Path.Combine(directory.Path, "fresh.jpg")));
        True(!System.IO.Directory.EnumerateFiles(directory.Path, "*.tmp").Any());
    }

    private static async Task DownloadFailure()
    {
        using var directory = new TemporaryDirectory();
        await using var server = new LoopbackServer();
        foreach (var (path, response) in new (string, LoopbackResponse)[]
        {
            ("http-error.jpg", new(503, [])),
            ("html.jpg", new(200, System.Text.Encoding.UTF8.GetBytes("<html>not a map</html>"))),
            ("oversized.jpg", new(200, [], AdvertisedLength: 17 * 1024 * 1024)),
        })
        {
            var target = System.IO.Path.Combine(directory.Path, path);
            File.WriteAllBytes(target, Jpeg);
            server.Responses["/" + path] = response;
            var download = new ImageDownloader([server.Url(path)], directory.Path);
            Equal(1, (await download.BeginDownloadAsync(replaceExisting: true)).Count);
            Sequence(Jpeg, File.ReadAllBytes(target));
        }
        True(!System.IO.Directory.EnumerateFiles(directory.Path, "*.tmp").Any());
    }

    private static async Task DownloadCancellation()
    {
        using var directory = new TemporaryDirectory();
        await using var server = new LoopbackServer();
        var target = System.IO.Path.Combine(directory.Path, "slow.jpg");
        File.WriteAllBytes(target, Jpeg);
        server.Responses["/slow.jpg"] = new(200, [], Slow: true);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var download = new ImageDownloader([server.Url("slow.jpg")], directory.Path);
        var running = download.BeginDownloadAsync(cancellation.Token, replaceExisting: true);
        await server.SlowRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await ThrowsAsync<OperationCanceledException>(async () => await running.WaitAsync(TimeSpan.FromSeconds(5)));
        Sequence(Jpeg, File.ReadAllBytes(target));
        True(!System.IO.Directory.EnumerateFiles(directory.Path, "*.tmp").Any());
    }

    private static async Task DownloadConcurrency()
    {
        using var directory = new TemporaryDirectory();
        await using var server = new LoopbackServer();
        var urls = Enumerable.Range(0, 12).Select(i =>
        {
            var path = $"concurrent-{i}.jpg";
            server.Responses["/" + path] = new(200, Jpeg, DelayMilliseconds: 150);
            return server.Url(path);
        }).ToList();
        var download = new ImageDownloader(urls, directory.Path);
        Equal(0, (await download.BeginDownloadAsync()).Count);
        Equal(12, download.CompletedDownloads);
        True(server.PeakConcurrentRequests is > 1 and <= 4, $"Observed concurrency: {server.PeakConcurrentRequests}");
    }

    private static void True(bool value, string? message = null)
    {
        if (!value) throw new InvalidOperationException(message ?? "Assertion failed.");
    }
    private static void Equal<T>(T expected, T actual) => True(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}.");
    private static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) => True(expected.SequenceEqual(actual), "Sequence mismatch.");
    private static void Near(float expected, float actual) => True(Math.Abs(expected - actual) < 0.002f, $"Expected approximately {expected}; got {actual}.");
    private static void Throws<T>(Action action) where T : Exception
    {
        try { action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }
    private static async Task ThrowsAsync<T>(Func<Task> action) where T : Exception
    {
        try { await action(); } catch (T) { return; }
        throw new InvalidOperationException($"Expected {typeof(T).Name}.");
    }

    private sealed class TrainFixture : IDisposable
    {
        public TemporaryDirectory Directory { get; } = new();
        public string FilePath => System.IO.Path.Combine(Directory.Path, "HuntTrain.json");
        public HuntTrainWorld VisitedWorld { get; set; } = WorldA;
        public TrainManager Manager { get; }
        public TrainFixture(string? initialFile = null)
        {
            if (initialFile != null) File.WriteAllText(FilePath, initialFile);
            Manager = new TrainManager(NullHost.Instance, NullHost.Instance, FilePath, () => VisitedWorld);
        }
        public TrainList<HuntTrainMob> Rows(uint world) { Manager.SelectWorld(world); return Manager.HuntTrain; }
        public void Dispose() { Manager.Dispose(); Directory.Dispose(); }
    }

    private sealed class NullHost : IChatGui, IGameGui
    {
        public static readonly NullHost Instance = new();
        public void Print(SeString _) => throw new InvalidOperationException("Unexpected chat access.");
        public void OpenMapWithMapLink(MapLinkPayload _) => throw new InvalidOperationException("Unexpected map UI access.");
    }
    private sealed record BattleNpc(uint NameId, Vector3 Position) : IBattleNpc
    {
        public SeString Name => new() { TextValue = $"Mob {NameId}" };
    }
}

internal sealed class TemporaryDirectory : IDisposable
{
    private static readonly string Parent = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HuntHelper.RegressionTests"));
    public string Path { get; } = System.IO.Path.Combine(Parent, Guid.NewGuid().ToString("N"));
    public TemporaryDirectory() => Directory.CreateDirectory(Path);
    public void Dispose()
    {
        // Validate the final absolute target before recursively removing only this test's generated files.
        var resolved = System.IO.Path.GetFullPath(Path);
        if (!resolved.StartsWith(Parent + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to clean a path outside the regression-test temp directory.");
        if (Directory.Exists(resolved)) Directory.Delete(resolved, true);
    }
}
