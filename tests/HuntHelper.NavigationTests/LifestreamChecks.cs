using HuntHelper.Managers.Automation;
using HuntHelper.Managers.Hunts.Models;

namespace HuntHelper.NavigationTests;

internal static class LifestreamChecks
{
    private static readonly HuntTrainWorld World=new(102,"银泪湖","",0);
    private static void Check(bool ok){if(!ok)throw new Exception("Lifestream IPC assertion failed");}
    private static (FakePlugin,LifestreamWorldTravel) Fixture()
    {
        var p=new FakePlugin();p.At("Lifestream.IsBusy").Function=(Func<bool>)(()=>false);
        p.At("Lifestream.CanVisitSameDC").Function=(Func<string,bool>)(w=>w=="银泪湖");
        p.At("Lifestream.CanVisitCrossDC").Function=(Func<string,bool>)(_=>false);
        p.At("Lifestream.ChangeWorldById").Function=(Func<uint,bool>)(id=>id==102);
        p.At("Lifestream.Abort").Action=(Action)(()=>{});
        return(p,new(p));
    }
    public static void Run(Action<string,Action> run)
    {
        run("CN Lifestream world requests use the exact uint-to-bool IPC contract",()=>
        {var(p,t)=Fixture();Check(t.Error=="" && t.Change(World,out _));Check(p.At("Lifestream.ChangeWorldById").Calls==1 && p.At("Lifestream.ChangeWorld").Calls==0);});
        run("Older Lifestream falls back to its exact localized name contract",()=>
        {var(p,t)=Fixture();p.At("Lifestream.ChangeWorldById").Function=null;p.At("Lifestream.ChangeWorld").Function=(Func<string,bool>)(w=>w=="银泪湖");Check(t.Change(World,out _) && p.At("Lifestream.ChangeWorld").Calls==1);});
        run("Missing Lifestream dependencies produce a download prompt without requesting travel",()=>
        {var(p,t)=Fixture();p.Available=false;Check(t.Error.Contains("下载") && !t.Change(World,out _) && p.TotalCalls==0);});
        run("Busy unsupported and rejected Lifestream requests do not receive a second request",()=>
        {var(p,t)=Fixture();p.At("Lifestream.IsBusy").Function=(Func<bool>)(()=>true);Check(!t.Change(World,out _) && p.At("Lifestream.ChangeWorldById").Calls==0);
         (p,t)=Fixture();p.At("Lifestream.CanVisitSameDC").Function=(Func<string,bool>)(_=>false);Check(!t.Change(World,out _) && p.At("Lifestream.ChangeWorldById").Calls==0);
         (p,t)=Fixture();p.At("Lifestream.ChangeWorldById").Function=(Func<uint,bool>)(_=>false);Check(!t.Change(World,out _) && p.At("Lifestream.ChangeWorld").Calls==0);});
        run("Owned travel cancellation invokes Abort only while Lifestream remains busy",()=>
        {var(p,t)=Fixture();t.AbortOwnedRequest();Check(p.At("Lifestream.Abort").Calls==0);p.At("Lifestream.IsBusy").Function=(Func<bool>)(()=>true);t.AbortOwnedRequest();Check(p.At("Lifestream.Abort").Calls==1);});
        run("Broken Lifestream IPC fails closed without throwing through the finder UI",()=>
        {var(p,t)=Fixture();p.At("Lifestream.IsBusy").Function=(Func<bool>)(()=>throw new Exception("missing"));Check(t.Busy && !t.Change(World,out _));
         p.At("Lifestream.CanVisitSameDC").Function=(Func<string,bool>)(_=>throw new Exception("missing"));Check(!t.Change(World,out var error) && error.Contains("不可用"));});
    }
}
