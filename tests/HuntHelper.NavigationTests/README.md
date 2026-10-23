# Navigation regression tests

Run `dotnet run --project tests/HuntHelper.NavigationTests/HuntHelper.NavigationTests.csproj` from the repository root.

The dependency-free .NET 10 console runner links the real `NavigationManager.cs` and `NavigationRules.cs`. Minimal host and IPC stubs simulate delayed successful tasks, cancellation, location changes, unavailable plugins and active movement. The fake pathfinding provider deliberately ignores cancellation so discarded late results are tested independently of provider cooperation.

These tests validate HuntHelper's request lifecycle and IPC call selection. They do not run Dalamud, test native pathfinding or replace an in-game check with the installed vnavmesh.
