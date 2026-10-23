# Headless regression tests

Run from the repository root:

```powershell
dotnet run --project tests/HuntHelper.RegressionTests/HuntHelper.RegressionTests.csproj -c Release
```

Requires the .NET 10 SDK and the existing `Newtonsoft.Json.dll` in the CN Dalamud development directory selected by the repository's `Directory.Build.props`. No test framework or additional NuGet packages are used.

The project links the real train manager, train/world models, sharing codec, navigation rules, and image downloader source files. It does not reference or build the plugin project, load game assemblies, or access the user's saved train file. Host services and sheet lookups are represented by the minimal boundaries in `Stubs.cs`; unexpected chat, map UI, or localization calls fail the tests.

The checks cover world identity and isolation, recording while browsing another world, import destination capture and replacement, 2D/3D update semantics, all-world persistence, legacy local/sharing data, explicit unknown-world metadata, damaged-file preservation, navigation location gates and coordinate inversion. Downloader checks use only an ephemeral `127.0.0.1` HTTP server and temporary files: cache reuse, successful writes, failed-refresh preservation, malformed/oversized payloads, cancellation cleanup, and concurrency limits.

JPEG fixtures exercise transport and marker validation, not image decoding. These tests do not verify live game rendering, Dalamud IPC binding, actual pathfinding, or VNavmesh behavior. Generated files are confined to unique subdirectories under the system temporary directory's `HuntHelper.RegressionTests` folder and removed after each check.
