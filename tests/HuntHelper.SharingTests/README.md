# Original/current share-code compatibility

Run `dotnet run --project tests/HuntHelper.SharingTests/ShareCompatibility.csproj -c Release` from the repository root.

The fixtures contain the original codec and model at the installed original HuntHelper 1.0.0.34 commit `c6b85581b5c2980214272a38527ee2aaf4b18e69`. Namespace declarations are changed so both versions can run side by side, and trailing comment whitespace is normalized. Implementation is unchanged. See `PROVENANCE.txt` and the repository's MIT `LICENSE` for attribution.

Checks use synthetic records to verify the nine legacy fields, Unicode names, record order, both export scopes, server isolation, original/current round trips, clipboard whitespace, invalid codes and empty lists. Current-world metadata and raw height survive current-to-current sharing. The original model cannot retain those additional fields when it re-exports a code. The original decoder accepts an all-server code as a flat list; original merge behavior cannot distinguish duplicate mob IDs from different worlds, so single-server sharing is recommended for original-plugin users.

The test process does not load the game or installed plugin DLL, or access player records. Its JSON report is saved to `obj/share-compatibility/verification.json`.
