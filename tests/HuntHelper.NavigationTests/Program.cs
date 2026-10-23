using System.Numerics;
using Dalamud.Plugin.Services;
using HuntHelper.Utilities;

namespace HuntHelper.NavigationTests;

internal static class Program
{
    private static int _passed;
    private static int _failed;

    private static int Main()
    {
        Run("Only a completed task on a Framework tick may start movement", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            Equal(1, f.PathfindRequests);
            Equal(f.Objects.LocalPlayer!.Position, f.LastFrom);
            Equal(f.Mob.WorldPosition!.Value, f.LastDestination);
            Equal(0, f.FloorQueries);
            Check(!f.LastFly, "ground navigation should be requested");
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
            f.Complete();
            Equal(0, f.MoveRequests);
            f.Framework.Tick();
            Equal(1, f.MoveRequests);
            Check(f.Manager.OwnsMovement, "movement should remain owned");
            Check(f.LastPath!.SequenceEqual(f.ValidPath()), "provider route should be used");
        });

        Run("Stop rejects a late successful task that ignores cancellation", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.Manager.Stop();
            Check(f.LastCancellation.IsCancellationRequested, "only this query should be cancelled");
            Check(!f.Manager.OwnsMovement, "ownership should clear immediately");
            f.Complete();
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
            Equal(0, f.StopRequests);
        });

        Run("Stop also rejects an already completed but unconsumed task", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.Complete();
            f.Manager.Stop();
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
        });

        LocationChange("Map changes", f => f.Client.MapId++);
        LocationChange("Territory changes", f => f.Client.TerritoryType++);
        LocationChange("World changes", f => f.PlayerState.CurrentWorld = new WorldReference(200));
        LocationChange("Instance changes", _ => MapHelpers.CurrentInstance++);
        LocationChange("Logout", f => f.Client.IsLoggedIn = false);
        LocationChange("Target marked dead", f => f.Mob.Dead = true);

        Run("Dispose cancels pending work, unsubscribes and rejects late success", () =>
        {
            using var f = new Fixture();
            Equal(1, f.Framework.SubscriberCount);
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.Manager.Dispose();
            Equal(0, f.Framework.SubscriberCount);
            Check(f.LastCancellation.IsCancellationRequested, "query should be cancelled on disposal");
            f.Complete();
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
            Check(!f.Manager.TryMoveTo(f.Mob), "disposed manager should reject new work");
        });

        Blocked("Missing vnavmesh", f => f.Plugin.Available = false);
        Blocked("Mesh not ready", f => f.Ready = false);
        Blocked("Another query in progress", f => f.Busy = true);
        Blocked("Another route running", f => f.Running = true);
        Blocked("Movement paused", f => f.MovementAllowed = false);
        Blocked("Unknown world metadata", f => f.Mob.WorldID = 0);
        Blocked("Different world at request time", f => f.Mob.WorldID++);
        Blocked("Different map at request time", f => f.Mob.MapID++);
        Blocked("Different instance at request time", f => f.Mob.Instance++);
        Blocked("Player unavailable", f => f.Objects.LocalPlayer = null);
        Blocked("Map loading", f => f.Client.MapId = 0);

        Run("Missing provider is detected without invoking absent IPC", () =>
        {
            using var f = new Fixture();
            f.Plugin.Available = false;
            Check(!f.Manager.TryMoveTo(f.Mob), "request must fail");
            Equal(0, f.Plugin.TotalCalls);
        });

        Run("Stopping active movement calls the Path.Stop action once", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.Complete();
            f.Framework.Tick();
            Equal(1, f.MoveRequests);
            f.Manager.Stop();
            Equal(1, f.StopRequests);
            Check(!f.Running && !f.Manager.OwnsMovement, "movement should be stopped");
            f.Manager.Stop();
            Equal(1, f.StopRequests);
        });

        Run("Dispose stops active movement and removes its callback", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.Complete();
            f.Framework.Tick();
            f.Manager.Dispose();
            Equal(1, f.StopRequests);
            Equal(0, f.Framework.SubscriberCount);
        });

        Run("Legacy map coordinates are projected onto the ground", () =>
        {
            using var f = new Fixture();
            f.Mob.WorldPosition = null;
            Check(f.Manager.TryMoveTo(f.Mob), "legacy request should be accepted");
            Equal(1, f.FloorQueries);
            Near(250, f.LastFloorProbe.X);
            Near(150, f.LastFloorProbe.Z);
            Near(1024, f.LastFloorProbe.Y);
            Check(!f.LastAllowUnlandable, "unlandable mesh should be excluded");
            Near(5, f.LastFloorRadius);
            Equal(f.Floor!.Value, f.LastDestination);
        });

        Run("A failed ground projection cannot start pathfinding", () =>
        {
            using var f = new Fixture();
            f.Mob.WorldPosition = null;
            f.Floor = null;
            Check(!f.Manager.TryMoveTo(f.Mob), "request must fail");
            Equal(0, f.PathfindRequests);
        });

        Run("A faulted provider task is observed without movement", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.NextPath.SetException(new InvalidOperationException("No route"));
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
            Check(!f.Manager.OwnsMovement, "failed request should release ownership");
        });

        Run("An empty provider route cannot start movement", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.NextPath.SetResult([]);
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
        });

        Run("A mesh invalidated before task consumption cannot start movement", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.Ready = false;
            f.Complete();
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
        });

        Run("A route started externally during our query is preserved", () =>
        {
            using var f = new Fixture();
            Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
            f.Running = true;
            f.Complete();
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
            Equal(0, f.StopRequests);
            Check(f.Running, "external route must not be stopped");
        });

        Run("A stopped old task cannot replace a later request", () =>
        {
            using var f = new Fixture();
            var oldTask = f.NextPath;
            Check(f.Manager.TryMoveTo(f.Mob), "first request should be accepted");
            f.Manager.Stop();
            f.NextPath = Fixture.NewPath();
            Check(f.Manager.TryMoveTo(f.Mob), "second request should be accepted");
            oldTask.SetResult([new Vector3(999, 999, 999)]);
            f.Framework.Tick();
            Equal(0, f.MoveRequests);
            f.Complete();
            f.Framework.Tick();
            Equal(1, f.MoveRequests);
            Check(f.LastPath!.SequenceEqual(f.ValidPath()), "only the current task should move");
        });

        Console.WriteLine($"Navigation regression tests: {_passed} passed, {_failed} failed.");
        return _failed == 0 ? 0 : 1;
    }

    private static void LocationChange(string name, Action<Fixture> change) => Run(name + " reject late success", () =>
    {
        using var f = new Fixture();
        Check(f.Manager.TryMoveTo(f.Mob), "request should be accepted");
        change(f);
        f.Complete();
        f.Framework.Tick();
        Equal(0, f.MoveRequests);
        Check(f.LastCancellation.IsCancellationRequested, "invalidated query should be cancelled");
        Check(!f.Manager.OwnsMovement, "invalidated request should release ownership");
        f.Client.MapId = 20;
        f.Client.TerritoryType = 10;
        f.PlayerState.CurrentWorld = new WorldReference(100);
        MapHelpers.CurrentInstance = 1;
        f.Framework.Tick();
        Equal(0, f.MoveRequests);
    });

    private static void Blocked(string name, Action<Fixture> change) => Run(name + " blocks pathfinding", () =>
    {
        using var f = new Fixture();
        change(f);
        Check(!f.Manager.TryMoveTo(f.Mob), "request should be blocked");
        Equal(0, f.PathfindRequests);
        Equal(0, f.MoveRequests);
    });

    private static void Run(string name, Action test)
    {
        try { test(); _passed++; Console.WriteLine("PASS " + name); }
        catch (Exception ex) { _failed++; Console.Error.WriteLine("FAIL " + name + ": " + ex); }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal<T>(T expected, T actual) where T : notnull =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"Expected {expected}; got {actual}");

    private static void Near(float expected, float actual) => Check(MathF.Abs(expected - actual) < 0.002f,
        $"Expected {expected}; got {actual}");
}
