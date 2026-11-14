using System;
using Dalamud.Plugin.Ipc;
using ECommons.DalamudServices;
namespace LuckyStar;

public sealed class GameRotationApi : IRotationApi, IDisposable
{
    public event Action<int,string>? LeaseLost;
    private readonly ICallGateProvider<int,string,object> _callback;
    public GameRotationApi()
    {
        _callback=Svc.PluginInterface.GetIpcProvider<int,string,object>("LuckyStarMOGAI.WrathComboCallback");
        _callback.RegisterAction((reason,info)=>LeaseLost?.Invoke(reason,info));
    }
    public void Dispose()=>_callback.UnregisterAction();
    public bool AeAvailable=>Svc.Commands.Commands.ContainsKey("/aeTargetSelector") && Svc.Commands.Commands.ContainsKey("/aepull");
    public bool Has(RotationCall call)=>call switch
    {
        RotationCall.RsrActive=>Svc.PluginInterface.GetIpcSubscriber<bool>("RotationSolverReborn.AutorotationActive").HasFunction,
        RotationCall.RsrMode=>Svc.PluginInterface.GetIpcSubscriber<string,object>("RotationSolverReborn.ChangeOperatingMode").HasAction,
        RotationCall.WrathReady=>Svc.PluginInterface.GetIpcSubscriber<bool>("WrathCombo.IPCReady").HasFunction,
        RotationCall.WrathJobReady=>Svc.PluginInterface.GetIpcSubscriber<bool>("WrathCombo.IsCurrentJobAutoRotationReady").HasFunction,
        RotationCall.WrathRegister=>Svc.PluginInterface.GetIpcSubscriber<string,string,string,Guid?>("WrathCombo.RegisterForLeaseWithCallback").HasFunction,
        RotationCall.WrathState=>Svc.PluginInterface.GetIpcSubscriber<Guid,bool,int>("WrathCombo.SetAutoRotationState").HasFunction,
        RotationCall.WrathConfig=>Svc.PluginInterface.GetIpcSubscriber<Guid,object,object,int>("WrathCombo.SetAutoRotationConfigState").HasFunction,
        RotationCall.WrathRelease=>Svc.PluginInterface.GetIpcSubscriber<Guid,object>("WrathCombo.ReleaseControl").HasAction,
        RotationCall.PromeActive=>Svc.PluginInterface.GetIpcSubscriber<bool>("PromeRotation.IPC.IsRunning").HasFunction,
        RotationCall.PromeStart=>Svc.PluginInterface.GetIpcSubscriber<bool>("PromeRotation.IPC.Start").HasFunction,
        RotationCall.PromeStop=>Svc.PluginInterface.GetIpcSubscriber<bool>("PromeRotation.IPC.Stop").HasFunction,
        RotationCall.PromeAcr=>Svc.PluginInterface.GetIpcSubscriber<string>("PromeRotation.IPC.GetCurrentAcrName").HasFunction,
        _=>false
    };
    public object? Invoke(RotationCall call,params object[] a)
    {
        switch(call)
        {
            case RotationCall.RsrActive:return Svc.PluginInterface.GetIpcSubscriber<bool>("RotationSolverReborn.AutorotationActive").InvokeFunc();
            case RotationCall.RsrMode:Svc.PluginInterface.GetIpcSubscriber<string,object>("RotationSolverReborn.ChangeOperatingMode").InvokeAction((string)a[0]);return null;
            case RotationCall.WrathReady:return Svc.PluginInterface.GetIpcSubscriber<bool>("WrathCombo.IPCReady").InvokeFunc();
            case RotationCall.WrathJobReady:return Svc.PluginInterface.GetIpcSubscriber<bool>("WrathCombo.IsCurrentJobAutoRotationReady").InvokeFunc();
            case RotationCall.WrathRegister:return Svc.PluginInterface.GetIpcSubscriber<string,string,string,Guid?>("WrathCombo.RegisterForLeaseWithCallback").InvokeFunc((string)a[0],(string)a[1],(string)a[2]);
            case RotationCall.WrathState:return Svc.PluginInterface.GetIpcSubscriber<Guid,bool,int>("WrathCombo.SetAutoRotationState").InvokeFunc((Guid)a[0],(bool)a[1]);
            case RotationCall.WrathConfig:return Svc.PluginInterface.GetIpcSubscriber<Guid,object,object,int>("WrathCombo.SetAutoRotationConfigState").InvokeFunc((Guid)a[0],a[1],a[2]);
            case RotationCall.WrathRelease:Svc.PluginInterface.GetIpcSubscriber<Guid,object>("WrathCombo.ReleaseControl").InvokeAction((Guid)a[0]);return null;
            case RotationCall.PromeActive:return Svc.PluginInterface.GetIpcSubscriber<bool>("PromeRotation.IPC.IsRunning").InvokeFunc();
            case RotationCall.PromeStart:return Svc.PluginInterface.GetIpcSubscriber<bool>("PromeRotation.IPC.Start").InvokeFunc();
            case RotationCall.PromeStop:return Svc.PluginInterface.GetIpcSubscriber<bool>("PromeRotation.IPC.Stop").InvokeFunc();
            case RotationCall.PromeAcr:return Svc.PluginInterface.GetIpcSubscriber<string>("PromeRotation.IPC.GetCurrentAcrName").InvokeFunc();
            default:throw new ArgumentOutOfRangeException(nameof(call));
        }
    }
    public void SetAe(bool on)
    {
        if(!on){try{Svc.Commands.ProcessCommand("/aeTargetSelector off");}finally{Svc.Commands.ProcessCommand("/aepull off");}return;}
        try
        {
            // FarmSession owns target selection so completed kinds cannot be selected by mode7.
            if(!AeAvailable || !Svc.Commands.ProcessCommand("/aeTargetSelector off") || !Svc.Commands.ProcessCommand("/aepull on"))
                throw new InvalidOperationException("AE 指定目标拉怪命令不可用");
        }
        catch{SetAe(false);throw;}
    }
}
