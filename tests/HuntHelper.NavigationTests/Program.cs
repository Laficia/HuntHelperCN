using System.Numerics;
using HuntHelper.Utilities;
namespace HuntHelper.NavigationTests;
internal static class Program
{
    private static int passed, failed;
    private static int Main()
    {
        LifestreamChecks.Run(Run);
        PendingStopChecks.Run(Run);
        OwnedPathChecks.Run(Run);
        Run("Native SimpleMove owns pending calculation and execution",()=>
        {using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); f.Framework.Tick(); Check(f.Manager.OwnsMovement&&f.Busy&&f.MoveRequests==1); f.NativeStart(); f.Framework.Tick(); Check(f.Manager.OwnsMovement); Check(f.Plugin.At("vnavmesh.Path.MoveTo").Calls==0&&f.Plugin.At("vnavmesh.Nav.PathfindCancelable").Calls==0);});
        Run("vnav stop while pathfinding never causes a late HHT restart",()=>
        {using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); f.NativeStop(); for(var i=0;i<10;i++)f.Framework.Tick(); Check(!f.Manager.OwnsMovement&&f.Manager.Interrupted&&f.MoveRequests==1&&f.StopRequests==0);});
        Run("vnav stop while moving clears ownership without restarting",()=>
        {using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); f.NativeStart(); f.Framework.Tick(); f.NativeStop(); f.Framework.Tick(); Check(!f.Manager.OwnsMovement&&f.Manager.Interrupted&&f.MoveRequests==1);});
        Run("Native manual-input cancellation is honored when enabled",()=>
        {using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); f.NativeStart(); f.NativeStop(); f.Framework.Tick(); Check(f.Manager.Interrupted&&!f.Manager.OwnsMovement);});
        Run("HHT imposes no input cancellation when native provider keeps moving",()=>
        {using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); f.NativeStart(); for(var i=0;i<4;i++)f.Framework.Tick(); Check(f.Manager.OwnsMovement&&f.StopRequests==0&&f.MoveRequests==1);});
        Run("HHT stop cancels native pending work and active motion exactly once",()=>
        {foreach(var started in new[]{false,true}){using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); if(started)f.NativeStart(); f.Manager.Stop(); f.Manager.Stop(); f.Framework.Tick(); Check(f.StopRequests==1&&!f.Busy&&!f.Running&&!f.Manager.OwnsMovement);}});
        Run("Unowned routes are never stopped",()=>
        {using var f=new Fixture(); f.Running=true; Check(!f.Manager.TryMoveTo(f.Mob)); f.Manager.Stop(); f.Manager.Dispose(); Check(f.StopRequests==0);});
        Run("Refused requests retain no ownership",()=>
        {using var f=new Fixture(); f.Accept=false; Check(!f.Manager.TryMoveTo(f.Mob)); f.Framework.Tick(); Check(!f.Manager.OwnsMovement&&f.StopRequests==0);});
        Run("Camera tolerance and movement settings are never overwritten",()=>
        {using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); f.Framework.Tick(); f.Manager.Stop(); foreach(var key in new[]{"SetMovementAllowed","SetAlignCamera","SetTolerance"})Check(f.Plugin.At("vnavmesh.Path."+key).Calls==0);});
        Run("Native movement pause prevents new requests",()=>
        {using var f=new Fixture(); f.MovementAllowed=false; Check(!f.Manager.TryMoveTo(f.Mob)&&f.MoveRequests==0);});
        Run("Flight mode is delegated unchanged",()=>
        {using var f=new Fixture(); f.Flying=true; Check(f.Manager.TryMoveTo(f.Mob)&&f.LastFly);});
        Run("Ground destination snaps to reachable native mesh",()=>
        {using var f=new Fixture(); f.Plugin.At("vnavmesh.Query.Mesh.NearestPointReachable").Function=(Func<Vector3,float,float,Vector3?>)((p,_,_)=>p with{Y=7}); Check(f.Manager.TryMoveTo(f.Mob)&&f.LastDestination.Y==7&&!f.LastFly);});
        Run("Map-only records resolve height before native movement",()=>
        {using var f=new Fixture(); f.Mob.WorldPosition=null; Check(f.Manager.TryMoveTo(f.Mob)&&f.FloorQueries==1&&f.LastDestination==f.Floor);});
        Run("Unresolved floors never move",()=>
        {using var f=new Fixture(); f.Mob.WorldPosition=null; f.Floor=null; Check(!f.Manager.TryMoveTo(f.Mob)&&f.MoveRequests==0);});
        Run("World map instance and death guards prevent wrong targets",()=>
        {using var f=new Fixture(); f.PlayerState.CurrentWorld=new(999); Check(!f.Manager.TryMoveTo(f.Mob)); f.PlayerState.CurrentWorld=new(100); f.Client.MapId=99; Check(!f.Manager.TryMoveTo(f.Mob)); f.Client.MapId=20; MapHelpers.CurrentInstance=2; Check(!f.Manager.TryMoveTo(f.Mob)); MapHelpers.CurrentInstance=1; f.Mob.Dead=true; Check(!f.Manager.TryMoveTo(f.Mob)&&f.MoveRequests==0);});
        Run("World transfer and logout cancel owned work",()=>
        {foreach(var logout in new[]{false,true}){using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); if(logout)f.Client.IsLoggedIn=false; else f.PlayerState.CurrentWorld=new(999); f.Framework.Tick(); Check(!f.Manager.OwnsMovement&&f.StopRequests==1);}});
        Run("Arrival clears ownership without interruption",()=>
        {using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); f.Objects.LocalPlayer!.Position=f.LastDestination; f.NativeStop(); f.Framework.Tick(); Check(!f.Manager.OwnsMovement&&!f.Manager.Interrupted&&f.StopRequests==0);});
        Run("Dispose cancels native pending work and removes update handler",()=>
        {using var f=new Fixture(); Check(f.Manager.TryMoveTo(f.Mob)); f.Manager.Dispose(); f.Framework.Tick(); Check(f.Framework.SubscriberCount==0&&f.StopRequests==1&&f.MoveRequests==1);});
        Console.WriteLine($"{passed} passed, {failed} failed");return failed==0?0:1;
    }
    private static void Check(bool value){if(!value)throw new Exception("Navigation assertion failed");}
    private static void Run(string name,Action action){try{action();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.Error.WriteLine("FAIL "+name+": "+ex);}}
}
