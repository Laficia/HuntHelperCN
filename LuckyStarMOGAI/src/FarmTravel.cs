namespace LuckyStar;

/// <summary>Waits for actual mount/flight flags, with throttled actions and bounded failure.</summary>
public sealed class FarmTravel(IFarmHost host)
{
    public string State { get; private set; } = "";
    public bool Failed { get; private set; }
    private string step="";
    private long started, nextAction;
    public void Reset(){step="";Failed=false;nextAction=0;}
    public bool Prepare(long now,bool fly)
    {
        var c=host.Context;
        if(c.Combat){State="等待脱战后上坐骑";return false;}
        if(!c.Mounted)return Wait("上坐骑",now,host.Mount);
        if(fly && !c.Diving && !c.Flying)return Wait("起飞",now,host.TakeOff);
        step="";return true;
    }
    public bool Dismount(long now)
    {
        if(!host.Context.Mounted && !host.Context.Flying){step="";return true;}
        return Wait("落地并下坐骑",now,host.Dismount);
    }
    private bool Wait(string value,long now,System.Action action)
    {
        if(step!=value){step=value;started=now;nextAction=now;}
        State=value;
        if(now-started>=15000){Failed=true;State=value+"超时，请检查坐骑、飞行解锁或当前位置";return false;}
        if(!host.Context.Casting && now>=nextAction){nextAction=now+1500;action();}
        return false;
    }
}
