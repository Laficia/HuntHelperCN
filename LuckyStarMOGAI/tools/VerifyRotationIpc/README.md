# Offline rotation IPC boundary verification

Run with the .NET 10 SDK and the installed CN Dalamud API 15 development files:

```powershell
dotnet run --project tools/VerifyRotationIpc/VerifyRotationIpc.csproj -c Release
```

The parent `Directory.Build.props` resolves `%APPDATA%\XIVLauncherCN\addon\Hooks\dev`. Override with `-p:DALAMUD_HOME=...` if needed.

This tool constructs private `CallGateChannel` objects and injects them into uninitialized instances of the **real installed generic `CallGatePubSub` implementation**, then invokes the public typed `ICallGateSubscriber` interfaces. Skipping the generic pubsub constructors avoids asking Dalamud's global IPC service for a channel. It does not instantiate RSR/Wrath, register global IPC, call a plugin interface, execute a chat command, connect to a game process, or control a character.

The eleven checks cover string-to-enum operating modes with differing byte layouts, enum-to-int success/error results, boxed object configuration option/int/bool values, nullable Guid leases, and enum-to-int callback conversion. All providers and enum types are local stand-ins. The checks verify serialization and typed subscriber compatibility, not real-plugin availability, a successful lease, correct job setup, game targeting, or combat behavior.

The private implementation names/properties are deliberately fail-fast. If a later SDK changes them, update the isolated test after reviewing that SDK; do not work around failures by calling live game IPC.
