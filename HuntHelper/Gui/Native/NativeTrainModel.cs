using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Gui.Native;

// Immutable UI snapshots cross from the ImGui/model tick to the native framework tick.
// Native event handlers only enqueue commands; they never mutate train storage.
internal sealed record NativeTrainTab(uint Id, string Text, bool Selected);
internal sealed record NativeTrainRow(string Key, uint Territory, uint Instance, string Map, string Name,
    bool Missing, bool Dead, bool Selected, bool CanTeleport, bool CanMove, string MoveReason, string Tooltip);
internal sealed record NativeTrainGroup(uint Territory, uint Instance, string Title, IReadOnlyList<NativeTrainRow> Rows);
internal sealed record NativeTrainSnapshot(bool Visible, string Rank, uint World, string CurrentWorld,
    bool Recording, bool Patrol, bool Moving, bool GroupByMap, string Status,
    IReadOnlyList<NativeTrainTab> Worlds, IReadOnlyList<NativeTrainTab> Versions, IReadOnlyList<NativeTrainRow> Rows)
{
    internal IReadOnlyList<NativeTrainGroup> Groups => Rows.GroupBy(row => (row.Territory, row.Instance))
        .Select(group => new NativeTrainGroup(group.Key.Territory, group.Key.Instance,
            group.First().Map + (group.Key.Instance == 0 ? "" : $" · {group.Key.Instance} 线"), group.ToArray())).ToArray();
}

internal enum NativeTrainAction
{
    Close, Finder, Recording, Macros, OtherRank, World, CurrentWorld, Version, Legacy,
    Select, Map, Teleport, Move, Coordinates, Dead, Import, Export, Backups, Settings,
    ClearDead, Revive, Clear, ReorderRow, ReorderWorld, ReorderVersion,
    SelectOnly, RowUp, RowDown, RemoveRow, ShiftWorld, DeleteWorld, ShiftVersion, ClearVersion,
}

internal sealed record NativeTrainRequest(NativeTrainAction Action, uint World = 0, string Key = "", string Target = "", bool Value = false);
