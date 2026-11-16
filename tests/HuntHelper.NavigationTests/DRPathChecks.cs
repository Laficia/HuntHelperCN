using System.Numerics;

namespace HuntHelper.NavigationTests;

internal static class DRPathChecks
{
    internal static void Run(Action<string, Action> run)
    {
        run("DR cancellable path uses player plus 2m and never invokes SimpleMove", () => {
            using var f = new Fixture(); var promise = new TaskCompletionSource<List<Vector3>>();
            CancellationToken token = default; Vector3 start = default; bool fly = false;
            f.Plugin.At("vnavmesh.Nav.PathfindCancelable").Function =
                (Func<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>)((from, to, flying, cancellation) => {
                    start = from; fly = flying; token = cancellation; return promise.Task; });
            Check(f.Manager.BeginCoveragePath(f.Mob)); f.Framework.Tick();
            Check(start == f.Objects.LocalPlayer!.Position + new Vector3(0, 2, 0) && fly && token.CanBeCanceled);
            Check(f.Manager.CoveragePathPending && f.Manager.OwnsMovement && f.MoveRequests == 0);
            var path = new List<Vector3> { new(10, 20, 30), new(40, 50, 60) };
            float tolerance = 0; bool executionFly = false; List<Vector3>? executed = null;
            f.Plugin.At("vnavmesh.Path.SetTolerance").Action = (Action<float>)(value => tolerance = value);
            f.Plugin.At("vnavmesh.Path.MoveTo").Action = (Action<List<Vector3>, bool>)((nodes, flying) => {
                executed = nodes; executionFly = flying; f.Running = true; });
            promise.SetResult(path); var result = f.Manager.TakeCoveragePath();
            Check(result!.SequenceEqual(path) && !ReferenceEquals(result, path));
            f.Manager.Stop(); Check(f.Manager.FollowCoveragePath(f.Mob, result!));
            Check(f.Plugin.At("vnavmesh.Path.SetTolerance").Calls == 1 && f.Plugin.At("vnavmesh.Path.MoveTo").Calls == 1 && f.MoveRequests == 0);
            Check(tolerance == 2 && executionFly && executed!.SequenceEqual(path) && !ReferenceEquals(executed, result));
        });
        run("DR stop cancels its token and late path results never execute", () => {
            using var f = new Fixture(); var promise = new TaskCompletionSource<List<Vector3>>(); CancellationToken token = default;
            f.Plugin.At("vnavmesh.Nav.PathfindCancelable").Function =
                (Func<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>)((_, _, _, cancellation) => { token = cancellation; return promise.Task; });
            Check(f.Manager.BeginCoveragePath(f.Mob)); f.Manager.Stop();
            Check(token.IsCancellationRequested && !f.Manager.OwnsMovement);
            promise.SetResult([new(30, 4, 5)]); f.Framework.Tick();
            Check(f.Manager.TakeCoveragePath() == null && f.Plugin.At("vnavmesh.Path.MoveTo").Calls == 0 && f.MoveRequests == 0);
        });
        run("DR faulted cancelled or null provider tasks fail without moving", () => {
            foreach (var mode in new[] { 0, 1, 2 }) {
                using var f = new Fixture();
                f.Plugin.At("vnavmesh.Nav.PathfindCancelable").Function =
                    (Func<Vector3, Vector3, bool, CancellationToken, Task<List<Vector3>>>)((_, _, _, _) => mode switch {
                        0 => Task.FromException<List<Vector3>>(new Exception("unreachable")),
                        1 => Task.FromCanceled<List<Vector3>>(new CancellationToken(true)), _ => null! });
                Check(f.Manager.BeginCoveragePath(f.Mob) == (mode != 2));
                Check(f.Manager.TakeCoveragePath() == null && f.MoveRequests == 0);
                f.Manager.Stop(); Check(!f.Manager.OwnsMovement);
            }
        });
        run("DR cannot dispatch a path after its server or map changes", () => {
            using var f = new Fixture(); Check(f.Manager.BeginCoveragePath(f.Mob));
            var result = f.Manager.TakeCoveragePath()!; f.Manager.Stop(); f.PlayerState.CurrentWorld = new(999);
            Check(!f.Manager.FollowCoveragePath(f.Mob, result) && f.Plugin.At("vnavmesh.Path.MoveTo").Calls == 0);
        });
        run("DR missing path interfaces and non-finite destinations cannot dispatch work", () => {
            using var f = new Fixture(); f.Plugin.At("vnavmesh.Path.MoveTo").Action = null;
            Check(!f.Manager.BeginCoveragePath(f.Mob));
            Check(f.Plugin.At("vnavmesh.Nav.PathfindCancelable").Calls == 0);
            using var invalid = new Fixture(); invalid.Mob.WorldPosition = new(float.NaN, 0, 0);
            Check(!invalid.Manager.BeginCoveragePath(invalid.Mob) && invalid.Plugin.At("vnavmesh.Nav.PathfindCancelable").Calls == 0);
        });
    }
    private static void Check(bool condition) { if (!condition) throw new Exception("DR path IPC contract failed"); }
}
