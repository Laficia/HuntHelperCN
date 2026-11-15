using System.Numerics;

// The actual upstream-derived controller is compiled into the regression runner.
// Only its Dalamud/action/chat boundaries are replaced; no movement algorithm is copied here.
namespace Dalamud.Plugin.Services { public interface IFramework { } }
namespace Dalamud.Game.ClientState.Conditions { public enum ConditionFlag { Mounted, InFlight } }
namespace Dalamud.Game.ClientState.Objects.Types
{
    public interface IBattleChara { uint NameId {get;} bool IsDead {get;} Vector3 Position {get;} }
}
namespace LuckyStar
{
    internal static class Plugin { public static Configuration Configuration=new(); }
    internal sealed record LegacyMob(uint NameId,Vector3 Position,bool IsDead):Dalamud.Game.ClientState.Objects.Types.IBattleChara;
    internal static class LegacyRuntime
    {
        public static readonly List<string> Commands=[];
        public static readonly List<uint> Actions=[];
        public static void Reset()
        {
            Commands.Clear();Actions.Clear();Plugin.Configuration=new();
            ECommons.DalamudServices.Svc.Objects.Clear();ECommons.DalamudServices.Svc.Objects.LocalPlayer=new();
            ECommons.DalamudServices.Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.Mounted]=true;
            ECommons.DalamudServices.Svc.Condition[Dalamud.Game.ClientState.Conditions.ConditionFlag.InFlight]=true;
        }
    }
}
namespace ECommons.DalamudServices
{
    internal sealed class LegacyFramework:Dalamud.Plugin.Services.IFramework
    {
        public event Action<Dalamud.Plugin.Services.IFramework>? Update;
        public void Tick()=>Update?.Invoke(this);
    }
    internal sealed class LegacyConditions
    {
        private readonly Dictionary<Dalamud.Game.ClientState.Conditions.ConditionFlag,bool> flags=[];
        public bool this[Dalamud.Game.ClientState.Conditions.ConditionFlag flag]{get=>flags.GetValueOrDefault(flag);set=>flags[flag]=value;}
    }
    internal sealed class LegacyPlayer { public Vector3 Position; }
    internal sealed class LegacyObjects:List<object> { public LegacyPlayer? LocalPlayer=new(); }
    internal static class Svc
    {
        public static readonly LegacyFramework Framework=new();
        public static readonly LegacyConditions Condition=new();
        public static readonly LegacyObjects Objects=new();
    }
}
namespace ECommons.Automation
{
    internal static class Chat { public static void ExecuteCommand(string command)=>LuckyStar.LegacyRuntime.Commands.Add(command); }
}
namespace FFXIVClientStructs.FFXIV.Client.Game
{
    internal enum ActionType { GeneralAction }
    internal unsafe struct ActionManager
    {
        private static readonly ActionManager* memory=(ActionManager*)System.Runtime.InteropServices.NativeMemory.AllocZeroed((nuint)sizeof(ActionManager));
        public static ActionManager* Instance()=>memory;
        public bool UseAction(ActionType type,uint id){LuckyStar.LegacyRuntime.Actions.Add(id);return true;}
        public static void ReleaseTestMemory()=>System.Runtime.InteropServices.NativeMemory.Free(memory);
    }
}
