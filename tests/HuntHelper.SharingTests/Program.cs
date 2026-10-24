using System.Numerics;
using System.Security.Cryptography;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using HuntHelper.Managers.Hunts;
using HuntHelper.Managers.Hunts.Models;
using Newtonsoft.Json;
using CurrentCodec = HuntHelper.Utilities.ExportImport;
using OriginalCodec = UpstreamOriginal.Utilities.ExportImport;
using OriginalMob = UpstreamOriginal.Models.HuntTrainMob;

internal static class Program
{
    private static readonly List<string> Passed = new();
    private static readonly DateTime Seen = new(2026, 10, 24, 1, 2, 3, DateTimeKind.Utc);
    private static int Main()
    {
        try
        {
            var original = new List<OriginalMob>
            {
                new("内丘奇霍", 13422, 1187, 862, 3, "遗产之地", new Vector2(27.9125f, 22.7125f), Seen, true),
                new("A 怪 '测试'", 13360, 1187, 862, 0, "Heritage Found", new Vector2(1.05f, 41.95f), Seen.AddMinutes(7), false)
            };
            var codeFromOriginal = OriginalCodec.Export(original);
            var importedByCurrent = CurrentCodec.Import(codeFromOriginal, new List<HuntTrainMob>());
            Equal(original.Count, importedByCurrent.Count);
            for (var i = 0; i < original.Count; i++) Same(original[i], importedByCurrent[i]);
            True(importedByCurrent.All(m => !m.HasWorldMetadata));
            Pass("Original 1.0.0.34 export -> current import: all legacy fields, Unicode and order preserved");

            var current = importedByCurrent.Select(m => m.WithWorld(new(1177, "银泪湖", "豆豆柴", 0))).ToList();
            current[0].WorldPosition = new Vector3(100, 25, -200);
            var currentCode = CurrentCodec.Export(current);
            var importedByOriginal = OriginalCodec.Import(currentCode, new List<OriginalMob>());
            Equal(current.Count, importedByOriginal.Count);
            for (var i = 0; i < current.Count; i++) Same(importedByOriginal[i], current[i]);
            Pass("Current export -> unchanged original decoder/model: extra world/3D fields do not block import");

            var originalAgain = OriginalCodec.Export(importedByOriginal);
            var currentAgain = CurrentCodec.Import(originalAgain, new List<HuntTrainMob>());
            for (var i = 0; i < currentAgain.Count; i++) Same(original[i], currentAgain[i]);
            True(currentAgain.All(m => !m.HasWorldMetadata && m.WorldPosition == null));
            Pass("Current -> original -> current round trip preserves all shared fields; unsupported metadata is absent");

            using var fixture = new Fixture();
            fixture.Manager.SelectWorld(102);
            fixture.Manager.Import(codeFromOriginal);
            fixture.Manager.SelectWorld(101); // Switching tabs after opening the dialog must not move the import.
            fixture.Manager.ImportTrainAll();
            fixture.Manager.SelectWorld(102);
            Equal(2, fixture.Manager.HuntTrain.Count);
            True(fixture.Manager.HuntTrain.All(m => m.WorldID == 102));
            for (var i = 0; i < original.Count; i++) Same(original[i], fixture.Manager.HuntTrain[i]);
            fixture.Manager.SelectWorld(101); Equal(0, fixture.Manager.HuntTrain.Count);
            Pass("Original codes import to the server selected when import begins; changing tabs does not rebind them");

            var forkAgain = CurrentCodec.Import(currentCode, new List<HuntTrainMob>());
            True(forkAgain.All(m => m.WorldID == 1177 && m.HasWorldMetadata));
            Equal(new Vector3(100, 25, -200), forkAgain[0].WorldPosition!.Value);
            Pass("Current-to-current codes retain server metadata and raw navigation height");

            var wrapped = " \r\n" + codeFromOriginal.Insert(codeFromOriginal.Length / 2, "\r\n") + "\n ";
            Equal(2, CurrentCodec.Import(wrapped, new List<HuntTrainMob>()).Count);
            var currentFallback = new List<HuntTrainMob>();
            var originalFallback = new List<OriginalMob>();
            True(ReferenceEquals(currentFallback, CurrentCodec.Import("invalid code", currentFallback)));
            True(ReferenceEquals(originalFallback, OriginalCodec.Import("invalid code", originalFallback)));
            Pass("Clipboard line wrapping is accepted and invalid codes retain the original fallback");

            var emptyCurrent = CurrentCodec.Export(Array.Empty<HuntTrainMob>());
            Equal(0, OriginalCodec.Import(emptyCurrent, new List<OriginalMob>()).Count);
            Equal(0, CurrentCodec.Import(OriginalCodec.Export(Array.Empty<OriginalMob>()), new List<HuntTrainMob>()).Count);
            Pass("Empty list sharing is accepted in both directions");

            fixture.Manager.SelectWorld(102);
            var singleScope = fixture.Manager.ExportSelectedTrain();
            Equal(2, OriginalCodec.Import(singleScope, new List<OriginalMob>()).Count);
            True(CurrentCodec.Import(singleScope, new List<HuntTrainMob>()).All(m => m.WorldID == 102));
            Pass("The single-server export API preserves its scope and the original decoder accepts its code");
            fixture.Manager.Import(currentCode); fixture.Manager.ImportTrainAll();
            var multiScope = fixture.Manager.ExportAllTrains(new uint[] { 1177, 102 });
            var originalMulti = OriginalCodec.Import(multiScope, new List<OriginalMob>());
            Equal(4, originalMulti.Count);
            var currentMulti = CurrentCodec.Import(multiScope, new List<HuntTrainMob>());
            True(currentMulti.Select(m => m.WorldID).SequenceEqual(new uint[] { 1177, 1177, 102, 102 }));
            for (var i = 0; i < 4; i++) Same(originalMulti[i], currentMulti[i]);
            Pass("All-server export retains distinct worlds in the current model and remains decodable by the original model");

            var report = new
            {
                CurrentVersion = "1.0.0.3",
                OriginalVersion = "1.0.0.34",
                OriginalCommit = "c6b85581b5c2980214272a38527ee2aaf4b18e69",
                Format = "Base64(GZip(UTF8(JSON array)))",
                Passed = Passed,
                Notes = "The original has no world or raw 3D fields; its re-export omits them. No game process or player records were used."
            };
            File.WriteAllText("obj/share-compatibility/verification.json", JsonConvert.SerializeObject(report, Formatting.Indented));
            Console.WriteLine($"{Passed.Count}/{Passed.Count} bidirectional compatibility groups passed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static void Same(OriginalMob expected, HuntTrainMob actual)
    {
        Equal(expected.Name, actual.Name); Equal(expected.MobID, actual.MobID);
        Equal(expected.TerritoryID, actual.TerritoryID); Equal(expected.MapID, actual.MapID);
        Equal(expected.Instance, actual.Instance); Equal(expected.MapName, actual.MapName);
        Equal(expected.Position, actual.Position); Equal(expected.LastSeenUTC, actual.LastSeenUTC);
        Equal(expected.Dead, actual.Dead);
    }
    private static void Pass(string name) { Passed.Add(name); Console.WriteLine("PASS " + name); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }

    private sealed class Fixture : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "HuntHelper.ShareCompatibility", Guid.NewGuid().ToString("N"));
        public TrainManager Manager { get; }
        public Fixture()
        {
            Directory.CreateDirectory(_dir);
            Manager = new TrainManager(new Host(), new Host(), Path.Combine(_dir, "HuntTrain.json"), () => new(101, "A服", "", 0));
        }
        public void Dispose()
        {
            Manager.Dispose();
            // Only delete this exact generated file, then the empty generated directory.
            File.Delete(Path.Combine(_dir, "HuntTrain.json"));
            Directory.Delete(_dir);
        }
    }
    private sealed class Host : IChatGui, IGameGui
    {
        public void Print(SeString _) => throw new Exception("Unexpected chat access");
        public void OpenMapWithMapLink(MapLinkPayload _) => throw new Exception("Unexpected game-map access");
    }
}
