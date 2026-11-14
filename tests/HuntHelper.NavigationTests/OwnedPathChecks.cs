using System.Numerics;
using HuntHelper.Utilities;

namespace HuntHelper.NavigationTests;

internal static class OwnedPathChecks
{
    public static void Run(Action<string, Action> run)
    {
        run("Path preview never reads unowned global routes", () =>
        {
            using var f = new Fixture();
            f.Running = true;
            f.Plugin.At("vnavmesh.Path.ListWaypoints").Function = (Func<List<Vector3>>)(() => [f.Mob.WorldPosition!.Value]);
            AssertReadOnly(f, false);
            Check(f.Plugin.At("vnavmesh.Path.ListWaypoints").Calls == 0);
        });
        run("Path preview returns a detached snapshot of native remaining nodes", () =>
        {
            using var f = Start();
            var nodes = new List<Vector3> { new(20, 25, 30), f.LastDestination };
            f.Plugin.At("vnavmesh.Path.ListWaypoints").Function = (Func<List<Vector3>>)(() => nodes);
            var snapshot = AssertReadOnly(f, true);
            Check(snapshot.SequenceEqual(nodes) && snapshot[0] != f.Objects.LocalPlayer!.Position);
            nodes.Clear();
            Check(snapshot.Count == 2 && snapshot[^1] == f.LastDestination);
        });
        run("Path preview hides stale executor nodes while native calculation is pending", () =>
        {
            using var f = Start();
            f.Busy = true;
            AssertReadOnly(f, false);
            Check(f.Plugin.At("vnavmesh.Path.ListWaypoints").Calls == 0);
        });
        run("Path preview handles missing IPC without changing the current move", () =>
        {
            using var f = Start();
            f.Plugin.At("vnavmesh.Path.ListWaypoints").Function = null;
            AssertReadOnly(f, false);
            Check(f.Manager.OwnsMovement && f.Running);
        });
        run("Path preview preserves partial native paths without inventing the requested final segment", () =>
        {
            using var f = Start();
            var native = new List<Vector3> { new(20, 25, 30), f.LastDestination + new Vector3(25, 0, 0) };
            f.Plugin.At("vnavmesh.Path.ListWaypoints").Function = (Func<List<Vector3>>)(() => native);
            var snapshot = AssertReadOnly(f, true);
            Check(snapshot.SequenceEqual(native) && !snapshot.Contains(f.LastDestination));
            f.Manager.GetOwnedPath(out var status);
            Check(status.Contains("vnav 当前路径") && status.Contains("终点距本次目标") && status.Contains("25") && status.Contains("米"));
            Check(f.Manager.OwnsMovement && f.Running);
        });
        run("Path preview allows small native destination adjustments", () =>
        {
            using var f = Start();
            f.Plugin.At("vnavmesh.Path.ListWaypoints").Function = (Func<List<Vector3>>)(() => [f.LastDestination + new Vector3(0, 9, 0)]);
            AssertReadOnly(f, true);
            f.Manager.GetOwnedPath(out var status);
            Check(status == "vnav 当前执行路径");
        });
        run("Path preview drops world territory map instance and logout mismatches without mutation", () =>
        {
            foreach (var change in new Action<Fixture>[] {
                f => f.PlayerState.CurrentWorld = new(999), f => f.Client.TerritoryType = 99,
                f => f.Client.MapId = 99, _ => MapHelpers.CurrentInstance = 2,
                f => f.Client.IsLoggedIn = false, f => f.Mob.Dead = true })
            {
                using var f = Start(); change(f); AssertReadOnly(f, false);
                Check(f.Plugin.At("vnavmesh.Path.ListWaypoints").Calls == 0 && f.StopRequests == 0);
            }
        });
        run("Path preview is empty immediately after native stop before the next update", () =>
        {
            using var f = Start(); f.NativeStop(); AssertReadOnly(f, false);
            Check(f.Manager.OwnsMovement && !f.Manager.Interrupted && f.StopRequests == 0);
            f.Framework.Tick(); AssertReadOnly(f, false);
            Check(!f.Manager.OwnsMovement && f.Manager.Interrupted);
        });
        run("Path preview rechecks scope after reading provider nodes", () =>
        {
            using var f = Start();
            f.Plugin.At("vnavmesh.Path.ListWaypoints").Function = (Func<List<Vector3>>)(() =>
            { MapHelpers.CurrentInstance = 2; return [f.LastDestination]; });
            AssertReadOnly(f, false);
        });
        run("Path preview stays empty after HHT stops or disposes ownership", () =>
        {
            using var f = Start(); f.Manager.Stop(); AssertReadOnly(f, false);
            f.Running = true; AssertReadOnly(f, false);
            f.Manager.Dispose(); AssertReadOnly(f, false);
        });
        run("Path preview catches provider exceptions without canceling or restarting", () =>
        {
            foreach (var endpoint in new[] { "vnavmesh.Path.ListWaypoints", "vnavmesh.Path.IsRunning", "vnavmesh.SimpleMove.PathfindInProgress" })
            {
                using var f = Start();
                f.Plugin.At(endpoint).Function = endpoint == "vnavmesh.Path.ListWaypoints"
                    ? (Func<List<Vector3>>)(() => throw new InvalidOperationException("test"))
                    : (Func<bool>)(() => throw new InvalidOperationException("test"));
                AssertReadOnly(f, false); Check(f.Manager.OwnsMovement && f.Running);
            }
        });
        run("Path preview rejects invalid empty null and oversized node lists", () =>
        {
            foreach (var nodes in new List<Vector3>?[] {
                null, [], [new(float.NaN, 0, 0), new(250, 50, 150)],
                [new(0, float.PositiveInfinity, 0), new(250, 50, 150)],
                Enumerable.Repeat(new Vector3(250, 50, 150), 4097).ToList() })
            {
                using var f = Start();
                f.Plugin.At("vnavmesh.Path.ListWaypoints").Function = (Func<List<Vector3>>)(() => nodes!);
                AssertReadOnly(f, false);
            }
        });
    }

    private static Fixture Start()
    {
        var f = new Fixture();
        Check(f.Manager.TryMoveTo(f.Mob)); f.NativeStart();
        f.Plugin.At("vnavmesh.Path.ListWaypoints").Function = (Func<List<Vector3>>)(() => [new(20, 25, 30), f.LastDestination]);
        return f;
    }

    private static IReadOnlyList<Vector3> AssertReadOnly(Fixture f, bool expected)
    {
        var moves = f.MoveRequests; var stops = f.StopRequests;
        var path = f.Manager.GetOwnedPath(out var status);
        Check((path.Count > 0) == expected && status.Length > 0);
        Check(f.MoveRequests == moves && f.StopRequests == stops);
        foreach (var endpoint in new[] {
            "vnavmesh.Nav.Pathfind", "vnavmesh.Nav.PathfindCancelable", "vnavmesh.Path.MoveTo",
            "vnavmesh.Path.SetTolerance", "vnavmesh.Path.SetMovementAllowed", "vnavmesh.Path.SetAlignCamera" })
            Check(f.Plugin.At(endpoint).Calls == 0);
        return path;
    }

    private static void Check(bool value)
    { if (!value) throw new InvalidOperationException("Owned path preview invariant failed"); }
}
