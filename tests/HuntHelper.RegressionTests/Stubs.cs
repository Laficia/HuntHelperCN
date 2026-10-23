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
    public sealed record MapLinkPayload(uint Territory, uint Map, float X, float Y);
}

namespace Dalamud.Plugin.Services
{
    public interface IChatGui { void Print(SeString message); }
    public interface IGameGui { void OpenMapWithMapLink(MapLinkPayload payload); }
    public interface IPlayerState { TestWorldReference CurrentWorld { get; } }
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
    public static class PluginLog { public static void Error(string _) { } }
    public static class LocalizationUtil { public static string GetInstanceGlyph(uint _) => string.Empty; }
    public static class MapHelpers
    {
        // Data-sheet localization is outside this regression harness.
        public static object? DataManager => null;
        public static void LocaliseMobNames(List<HuntTrainMob> _) =>
            throw new InvalidOperationException("Unexpected game-data access in headless test.");
        public static Vector2 ConvertToMapCoordinate(Vector3 position, float factor) =>
            new(2048f / factor + position.X / 50f + 1, 2048f / factor + position.Z / 50f + 1);
    }
}
