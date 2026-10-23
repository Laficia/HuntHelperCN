// Minimal stand-ins for the host services. The navigation implementation and
// location/coordinate rules are linked from the actual plugin sources.
using System.Numerics;

namespace Dalamud.Plugin.Ipc
{
    public interface ICallGateSubscriber
    {
        bool HasFunction { get; }
        bool HasAction { get; }
    }

    public interface ICallGateSubscriber<TRet> : ICallGateSubscriber
    {
        TRet InvokeFunc();
        void InvokeAction();
    }

    public interface ICallGateSubscriber<T1, T2, TRet> : ICallGateSubscriber
    {
        void InvokeAction(T1 first, T2 second);
    }

    public interface ICallGateSubscriber<T1, T2, T3, TRet> : ICallGateSubscriber
    {
        TRet InvokeFunc(T1 first, T2 second, T3 third);
    }

    public interface ICallGateSubscriber<T1, T2, T3, T4, TRet> : ICallGateSubscriber
    {
        TRet InvokeFunc(T1 first, T2 second, T3 third, T4 fourth);
    }
}

namespace Dalamud.Plugin
{
    using Dalamud.Plugin.Ipc;

    public interface IDalamudPluginInterface
    {
        ICallGateSubscriber<TRet> GetIpcSubscriber<TRet>(string name);
        ICallGateSubscriber<T1, T2, TRet> GetIpcSubscriber<T1, T2, TRet>(string name);
        ICallGateSubscriber<T1, T2, T3, TRet> GetIpcSubscriber<T1, T2, T3, TRet>(string name);
        ICallGateSubscriber<T1, T2, T3, T4, TRet> GetIpcSubscriber<T1, T2, T3, T4, TRet>(string name);
    }
}

namespace Dalamud.Plugin.Services
{
    public interface IClientState
    {
        bool IsLoggedIn { get; }
        uint TerritoryType { get; }
        uint MapId { get; }
    }

    public readonly record struct WorldReference(uint RowId);

    public interface IPlayerState
    {
        bool IsLoaded { get; }
        WorldReference CurrentWorld { get; }
    }

    public interface IObjectTable
    {
        PlayerObject? LocalPlayer { get; }
    }

    public sealed class PlayerObject
    {
        public Vector3 Position { get; set; }
    }

    public interface IFramework
    {
        event Action<IFramework> Update;
    }

    public interface IDataManager
    {
        ExcelSheet<T> GetExcelSheet<T>() where T : struct;
    }

    public sealed class ExcelSheet<T> where T : struct
    {
        public T? Row { get; set; }
        public T? GetRowOrDefault(uint rowId) => Row;
    }
}

namespace Lumina.Excel.Sheets
{
    public readonly record struct Map(ushort SizeFactor);
}

namespace HuntHelper.Managers.Hunts.Models
{
    public sealed class HuntTrainMob
    {
        public string Name { get; set; } = "Test A rank";
        public string WorldName { get; set; } = "Test World";
        public uint WorldID { get; set; } = 100;
        public uint TerritoryID { get; set; } = 10;
        public uint MapID { get; set; } = 20;
        public uint Instance { get; set; } = 1;
        public bool Dead { get; set; }
        public Vector2 Position { get; set; } = new(26.48f, 24.48f);
        public Vector3? WorldPosition { get; set; } = new Vector3(250, 50, 150);
    }
}

namespace HuntHelper.Utilities
{
    public static class MapHelpers
    {
        public static uint CurrentInstance { get; set; } = 1;
    }

    public static class PluginLog
    {
        public static void Warning(Exception exception, string message) { }
    }
}
