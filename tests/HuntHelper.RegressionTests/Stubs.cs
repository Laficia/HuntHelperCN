// Deliberately minimal host boundaries. No Dalamud or game assembly is loaded by these tests.
using System.Numerics;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using HuntHelper.Managers.Hunts.Models;

namespace Dalamud.Bindings.ImGui
{
    [Flags] public enum ImGuiWindowFlags { NoScrollbar = 1, NoTitleBar = 2 }
}

namespace Dalamud.Configuration
{
    public interface IPluginConfiguration { int Version { get; set; } }
}

namespace Dalamud.Plugin
{
    public interface IDalamudPluginInterface { void SavePluginConfig(Dalamud.Configuration.IPluginConfiguration config); }
}

namespace Dalamud.Game.ClientState.Objects.Types
{
    public interface IBattleNpc
    {
        uint NameId { get; }
        SeString Name { get; }
        Vector3 Position { get; }
    }
}

namespace Dalamud.Game.Text.SeStringHandling
{
    public sealed class SeString
    {
        public string TextValue { get; init; } = string.Empty;
        public List<object> Payloads { get; } = new();
        public static SeString CreateMapLink(uint territory, uint map, float x, float y)
        {
            var result = new SeString();
            result.Payloads.Add(new MapLinkPayload(territory, map, x, y));
            return result;
        }
    }

    public sealed class SeStringBuilder
    {
        public SeString BuiltString { get; } = new();
        public void AddUiForeground(ushort _) { }
        public void AddUiForegroundOff() { }
        public void AddText(string _) { }
        public void AddIcon(BitmapFontIcon _) { }
        public void Append(SeString _) { }
    }
}

namespace Dalamud.Game.Text.SeStringHandling.Payloads
{
    public enum BitmapFontIcon { ExclamationRectangle }
    public sealed record MapLinkPayload(uint Territory, uint MapId, float X, float Y)
    {
        public TestRow Map => new(MapId); public TestRow TerritoryType => new(Territory);
        public float XCoord => X; public float YCoord => Y;
        public int RawX => (int)(X * 1000); public int RawY => (int)(Y * 1000);
    }
    public sealed record TestRow(uint RowId);
    public sealed record TextPayload(string Text);
    public sealed record DalamudLinkPayload(string Plugin);
}

namespace Dalamud.Plugin.Services
{
    public interface IChatGui { void Print(SeString message); event Action<Dalamud.Game.Chat.IHandleableChatMessage> ChatMessage { add {} remove {} } }
    public interface IGameGui { void OpenMapWithMapLink(MapLinkPayload payload); }
    public interface IPlayerState { TestWorldReference CurrentWorld { get; } bool IsLoaded => true; }
    public sealed record TestWorldReference(uint RowId, TestWorldRow? ValueNullable);
    public sealed record TestWorldRow(string Name, TestDataCenterReference DataCenter);
    public sealed record TestDataCenterReference(TestDataCenterRow? ValueNullable);
    public sealed record TestDataCenterRow(string Name);
}

namespace HuntHelper.Managers
{
    public struct AetheryteData { public Vector2 Position; }
}

namespace HuntHelper.Utilities
{
    public static class PluginLog { public static void Error(string _) { } public static void Warning(Exception _, string __) {} }
    public static class LocalizationUtil { public static string GetInstanceGlyph(this uint i) => i is >=1 and <=9 ? ((char)(0xe0b0+i)).ToString() : string.Empty; }
    public static class MapHelpers
    {
        public static IReadOnlyList<HuntTrainWorld> GetImportableWorlds(uint _) => new[] { new HuntTrainWorld(101,"银泪湖","",0), new HuntTrainWorld(102,"水晶塔","",0) };
        public static float MapScaleToMaxCoord(uint _) => 41;
        public static string GetMapName(uint _) => "地图";
        // Data-sheet localization is outside this regression harness.
        public static object? DataManager => null;
        public static void LocaliseMobNames(List<HuntTrainMob> _) =>
            throw new InvalidOperationException("Unexpected game-data access in headless test.");
        public static Vector2 ConvertToMapCoordinate(Vector3 position, float factor) =>
            new(2048f / factor + position.X / 50f + 1, 2048f / factor + position.Z / 50f + 1);
    }
}

namespace Dalamud.Game.Chat
{
    public interface IHandleableChatMessage { SeString Sender { get; } SeString Message { get; } }
}
