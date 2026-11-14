// LuckyStarMOGAI, 2026-11-14. AGPL-3.0-or-later.
using System;
using System.Collections.Generic;

namespace LuckyStar;

public enum RotationProvider { AE, RotationSolverReborn, WrathCombo, Prome }
public enum RotationCall
{
    RsrActive, RsrMode, WrathReady, WrathJobReady, WrathRegister, WrathState,
    WrathConfig, WrathRelease, PromeActive, PromeStart, PromeStop, PromeAcr
}
public interface IRotationApi
{
    bool Has(RotationCall call);
    object? Invoke(RotationCall call, params object[] arguments);
    bool AeAvailable { get; }
    void SetAe(bool enabled);
    event Action<int,string>? LeaseLost;
}

/// <summary>Only the chosen engine is controlled. Wrath uses a native lease; RSR/Prome have no lease API.</summary>
public sealed class CombatRotationSession(IRotationApi api) : IDisposable
{
    public static readonly string[] Names = ["AE（原版）", "RotationSolverReborn", "Wrath Combo", "PromeRotation"];
    public RotationProvider Provider { get; private set; }
    public string Message { get; private set; } = "尚未开始";
    public bool Begun { get; private set; }
    private bool _enabled, _ownsSwitch, _releasing, _allowStart;
    private Guid? _lease;
    private volatile bool _revoked;
    private string _revocation = "";

    public string Availability(RotationProvider provider)
    {
        RotationCall[] required = provider switch
        {
            RotationProvider.RotationSolverReborn => [RotationCall.RsrActive,RotationCall.RsrMode],
            RotationProvider.WrathCombo => [RotationCall.WrathReady,RotationCall.WrathJobReady,RotationCall.WrathRegister,
                RotationCall.WrathState,RotationCall.WrathConfig,RotationCall.WrathRelease],
            RotationProvider.Prome => [RotationCall.PromeActive,RotationCall.PromeStart,RotationCall.PromeStop,RotationCall.PromeAcr],
            _ => []
        };
        if(provider==RotationProvider.AE && !api.AeAvailable) return "请启用支持 /aeTargetSelector 和 /aepull 的 AE";
        foreach(var call in required) if(!api.Has(call)) return provider switch
        {
            RotationProvider.RotationSolverReborn => "RSR 未加载或缺少状态接口；需要支持 AutorotationActive / ChangeOperatingMode 的国服兼容版",
            RotationProvider.WrathCombo => "Wrath 未加载或缺少租约接口，请使用兼容国服 API 15 的版本并开启 IPC",
            _ => "Prome 未加载或缺少公开控制接口，请使用兼容国服 API 15 的版本"
        };
        return "";
    }

    public void Begin(RotationProvider provider, bool allowStart=false)
    {
        if(Begun || _lease!=null || _ownsSwitch || _enabled) throw new InvalidOperationException("已有循环控制尚未释放，请先执行 /luckystarmogai stop");
        var unavailable=Availability(provider);
        if(unavailable.Length>0) throw new InvalidOperationException(unavailable);
        Provider=provider;_enabled=_ownsSwitch=_revoked=false;Message="已选择 "+Names[(int)provider];
        _allowStart=allowStart;
        if(provider==RotationProvider.Prome && string.IsNullOrWhiteSpace((string?)api.Invoke(RotationCall.PromeAcr)))
            throw new InvalidOperationException("请先在 Prome 中选择并配置当前职业 ACR");
        if(provider is RotationProvider.RotationSolverReborn or RotationProvider.Prome && !allowStart && !Active())
            throw new InvalidOperationException("请勾选「一键托管循环」，或先在所选插件中手动开启循环");
        if(provider==RotationProvider.WrathCombo)
        {
            if(!(bool)api.Invoke(RotationCall.WrathReady)!) throw new InvalidOperationException("Wrath IPC 尚未就绪，请在 Wrath 中允许插件控制");
            if(!(bool)api.Invoke(RotationCall.WrathJobReady)!) throw new InvalidOperationException("请先在 Wrath 中启用当前职业的单体／群体组合及 Auto-Mode");
            api.LeaseLost+=OnLeaseLost;
            try
            {
                _lease=(Guid?)api.Invoke(RotationCall.WrathRegister,"LuckyStarMOGAI","LuckyStarMOGAI","LuckyStarMOGAI");
                if(_lease==null) throw new InvalidOperationException("Wrath 未授予控制租约，请检查 IPC 设置和授权");
                // Wrath requires a state registration before any config registration.
                Check(api.Invoke(RotationCall.WrathState,_lease.Value,false));
                Check(api.Invoke(RotationCall.WrathConfig,_lease.Value,0,false));
                Check(api.Invoke(RotationCall.WrathConfig,_lease.Value,13,false));
                Check(api.Invoke(RotationCall.WrathConfig,_lease.Value,1,0));
            }
            catch { try{ReleaseLease();}finally{api.LeaseLost-=OnLeaseLost;}throw; }
        }
        Begun=true;
    }

    public void SetCombat(bool enabled)
    {
        if(!Begun) { if(enabled)throw new InvalidOperationException("自动循环尚未准备");return; }
        if(_enabled==enabled) return;
        if(_revoked)throw new InvalidOperationException(_revocation);
        switch(Provider)
        {
            case RotationProvider.AE: api.SetAe(enabled);break;
            case RotationProvider.WrathCombo:
                if(_lease is not { } lease)throw new InvalidOperationException("Wrath 控制租约已失效");
                Check(api.Invoke(RotationCall.WrathState,lease,enabled));break;
            case RotationProvider.RotationSolverReborn:
            case RotationProvider.Prome:
                if(enabled)
                {
                    if(Active()) Message="沿用已开启的循环；本插件不会关闭它";
                    else
                    {
                        if(!_allowStart)throw new InvalidOperationException("所选循环已停止，请手动开启后重新开始农怪");
                        // Mark ownership before a call that can fail after changing state, so cleanup is reliable.
                        _ownsSwitch=true;
                        if(Provider==RotationProvider.RotationSolverReborn) api.Invoke(RotationCall.RsrMode,"Henched");
                        else if(!(bool)api.Invoke(RotationCall.PromeStart)!)
                        { _ownsSwitch=false;throw new InvalidOperationException("Prome 拒绝启动，请检查当前 ACR 和插件自身的状态提示"); }
                        if(!Active())throw new InvalidOperationException("自动循环未启动成功，请检查所选插件的职业和状态设置");
                        Message="本插件临时开启循环；停止时关闭";
                    }
                }
                else CloseOwnedSwitch();
                break;
        }
        _enabled=enabled;
    }

    // Called only while the player/scene are ready: RSR/Prome getters can return false during loading.
    public string? CheckWhileReady()
    {
        if(!Begun)return null;
        if(_revoked){_lease=null;return _revocation;}
        var unavailable=Availability(Provider);if(unavailable.Length>0)return unavailable;
        if(_enabled && Provider is RotationProvider.RotationSolverReborn or RotationProvider.Prome && !Active())
        { return "所选自动循环已停止或暂不可用，农怪已停止；不会自动重新开启"; }
        return null;
    }

    public void End()
    {
        try
        {
            if(Provider==RotationProvider.WrathCombo) ReleaseLease();
            else if(Provider==RotationProvider.AE) { if(_enabled)api.SetAe(false); }
            else CloseOwnedSwitch();
        }
        finally { api.LeaseLost-=OnLeaseLost;Begun=_enabled=false; }
    }
    public void Dispose()=>End();
    private bool Active()=>(bool)api.Invoke(Provider==RotationProvider.Prome?RotationCall.PromeActive:RotationCall.RsrActive)!;
    private void CloseOwnedSwitch()
    {
        if(!_ownsSwitch)return;
        if(Provider==RotationProvider.RotationSolverReborn)api.Invoke(RotationCall.RsrMode,"Off");
        else if(!(bool)api.Invoke(RotationCall.PromeStop)!)throw new InvalidOperationException("Prome 未确认停止，请检查插件状态");
        _ownsSwitch=false;
    }
    private void ReleaseLease()
    {
        var lease=_lease;_lease=null;
        if(lease==null || _revoked)return;
        _releasing=true;
        try { api.Invoke(RotationCall.WrathRelease,lease.Value); }
        catch { if(!_revoked)_lease=lease;throw; }
        finally { _releasing=false; }
    }
    private void OnLeaseLost(int reason,string info)
    {
        // Wrath calls back before deleting its registration. Never re-enter ReleaseControl here.
        if(_releasing && reason==3)return;
        _revocation=$"Wrath 控制已结束（{reason}）：{info}；农怪停止后请手动重新开始";
        _revoked=true;
    }
    private static void Check(object? result)
    {
        if(result==null)throw new InvalidOperationException("Wrath 未返回有效控制结果");
        var code=Convert.ToInt32(result);
        if(code is not (0 or 1 or 13))throw new InvalidOperationException($"Wrath 拒绝控制请求（代码 {code}），请检查 IPC、租约和职业配置");
    }
}
