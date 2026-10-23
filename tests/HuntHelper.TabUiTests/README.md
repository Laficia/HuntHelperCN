# Native server tab interaction checks

Run `dotnet run --project tests/HuntHelper.TabUiTests -c Release` on Windows with the CN Dalamud development assemblies available through `DALAMUD_HOME`.

This console runner links the production `WorldTabBar` and `WorldTabOrder` and uses the installed CN `Dalamud.Bindings.ImGui.dll` plus `cimgui.dll` in an isolated ImGui context. It builds frames, injects mouse position/button events, drags tabs in both directions, compares actual item rectangles against saved order, and clicks the real right-click menu entries for selecting, left/right movement and deletion. It also checks removal of empty tabs and the absence of a native drag/drop payload, which caused the default yellow target.

It creates no desktop window, uses no game process or GPU backend, and disables ImGui ini persistence. It does not replace an in-game display check.

Window checks link the production `HuntTrainWindowLayout`, drag the real native lower-right resize grip smaller and larger, verify the compact minimum, click the real Restore default size button, and check scaled limits against the available viewport.
