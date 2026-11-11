# Navigation regression tests

Run `dotnet run --project tests/HuntHelper.NavigationTests/HuntHelper.NavigationTests.csproj` from the repository root.

The dependency-free .NET 10 console runner links the real `NavigationManager.cs` and `NavigationRules.cs`. Minimal host and IPC stubs simulate native SimpleMove pending/running/stopped states, provider refusal, floor resolution, location changes, disposal and movement preferences. HHT no longer owns an independent path Task or submits Path.MoveTo. Provider cancellation is honored with both native input-cancel on and off, without rewriting settings.

These tests validate HuntHelper's request lifecycle and IPC call selection. They do not run Dalamud, test native pathfinding or replace an in-game check with the installed vnavmesh.

Lifestream checks link the production WorldTravel adapter and verify exact uint/bool IPC and localized-name fallback, missing/busy/unsupported/rejected interfaces, cancellation, and broken-function behavior. They use fake gates only; installed CN 2.5.4.15 signatures were separately inspected as metadata, without executing the plugin.


Installed CN 0.7.7.0 metadata shows PluginIPC Path.Stop and /vnav stop call only MovementPlanExecutor.Stop. The compatibility resolver uses the loaded command target's AsyncMoveRequest public Stop to also cancel pending native calculation; the observer preserves the original chat handler and ignores unowned routes. Additional cases cover exact stop matching, absent full-cancel fail-closed behavior, scoped cancellation and patrol interruption. The native chat hook itself requires in-game verification.
