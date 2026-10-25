# Native server tab interaction checks

Run `dotnet run --project tests/HuntHelper.TabUiTests -c Release` on Windows with the CN Dalamud development assemblies available through `DALAMUD_HOME`.

This console runner links the production `WorldTabBar` and `WorldTabOrder` and uses the installed CN `Dalamud.Bindings.ImGui.dll` plus `cimgui.dll` in an isolated ImGui context. It builds frames, injects mouse position/button events, drags tabs in both directions, compares actual item rectangles against saved order, and clicks the real right-click menu entries for selecting, left/right movement and deletion. It also checks removal of empty tabs and the absence of a native drag/drop payload, which caused the default yellow target.

It creates no desktop window, uses no game process or GPU backend, and disables ImGui ini persistence. It does not replace an in-game display check.

Window checks link the production `HuntTrainWindowLayout`, drag the real native lower-right resize grip smaller and larger, verify the compact minimum, click the real Restore default size button, and check scaled limits against the available viewport.

Roster presentation checks exercise the production summary and status badges and ensure their scoped styles are restored. On Windows, pass --render-roster (optionally --show-missing) to render a Chinese preview using simulated records and the Microsoft YaHei font installed in Windows. The offscreen CPU renderer writes obj/release-staging/v1.0.0.6/roster-preview-default.png; this is a layout preview, not an in-game screenshot.

Version and server bars share TabDragGesture. Checks drag versions both ways, reload the order, verify no native payload, click Start and Stop at the same position, and verify a scaled window can shrink below a host minimum of 420 pixels.

Record-row checks link the production HuntRowDragGesture and inject real ImGui mouse/key events. They verify plain click selection, insertion both directions, no click activation while dragging, no-op/outside drops, Escape cancellation, cancellation after world/version/list changes, long-table edge scrolling, and absence of native yellow drag/drop payloads.
