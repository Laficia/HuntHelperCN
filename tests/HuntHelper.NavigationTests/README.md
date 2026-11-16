# Navigation regression tests

Run `dotnet run --project tests/HuntHelper.NavigationTests/HuntHelper.NavigationTests.csproj` from the repository root.

The dependency-free .NET 10 console runner links the real `NavigationManager.cs` and `NavigationRules.cs`. Minimal host and IPC stubs simulate native SimpleMove pending/running/stopped states, provider refusal, floor resolution, location changes, disposal and movement preferences. Ordinary monster movement uses native SimpleMove and never restarts cancelled work. DR patrol separately uses cancellable path calculation and Path.MoveTo with the original two-meter tolerance; its controller retries interruption until HHT Stop.

These tests validate HuntHelper's request lifecycle and IPC call selection. They do not run Dalamud, test native pathfinding or replace an in-game check with the installed vnavmesh.

DR FastWorldTravel checks exercise the nullable module query, numeric world command, cross-data-center dependency, long logout/loading transitions, stable actual arrival, command refusal and queue release without inventing a DR cancellation API. The old Lifestream adapter cases remain historical compatibility coverage; active HHT world travel uses DR.

DR path tests cover the exact cancellable-task IPC, player-plus-two-meter origin, flight execution and tolerance, independent path copies, faulted/cancelled/missing tasks, final stop with late results, missing interfaces and world changes. Coverage-controller tests in the regression suite check truncation, retry geometry, timeouts and final HHT stop.


Installed CN 0.7.7.0 metadata shows PluginIPC Path.Stop and /vnav stop call only MovementPlanExecutor.Stop. The compatibility resolver uses the loaded command target's AsyncMoveRequest public Stop to also cancel pending native calculation; the observer preserves the original chat handler and ignores unowned routes. Additional cases cover exact stop matching, absent full-cancel fail-closed behavior, scoped cancellation and patrol interruption. The native chat hook itself requires in-game verification.
