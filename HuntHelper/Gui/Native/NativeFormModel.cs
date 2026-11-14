using System;
using System.Collections.Generic;

namespace HuntHelper.Gui.Native;

internal enum NativeFormKind { Label, Heading, Check, Number, Choice, Buttons, CheckList, Collection, Pages, Input, Multiline }
internal sealed record NativeFormChoice(string Id, string Label, bool Selected = false, string Tooltip = "", bool Enabled = true);
internal sealed record NativeFormRow(string Key, string Label, NativeFormKind Kind, string Value = "",
    bool Enabled = true, IReadOnlyList<NativeFormChoice>? Choices = null, int Min = 0, int Max = 100, string Tooltip = "");
internal sealed record NativeFormSnapshot(bool Visible, IReadOnlyList<NativeFormChoice> Toolbar,
    IReadOnlyList<NativeFormRow> Rows, string Status = "")
{
    internal static NativeFormSnapshot Hidden { get; } = new(false, Array.Empty<NativeFormChoice>(), Array.Empty<NativeFormRow>());
}
internal sealed record NativeFormRequest(string Key, string Value = "", string Target = "", bool After = true);
