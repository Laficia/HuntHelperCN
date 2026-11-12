using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

internal readonly record struct MacroDrop(string Id, string? Target, bool After);
internal readonly record struct MacroItemBounds(string Id, Vector2 Min, Vector2 Max);

/// <summary>Collection and page tabs share one gesture; no native payload, yellow target or click-on-drop.</summary>
internal sealed class MacroCollectionDrag
{
    private string? _source;
    private bool _cancelled;
    private string _scope = "";
    private readonly List<MacroItemBounds> _targets = new();
    private (Vector2 Min, Vector2 Max)? _append;
    public bool Dragging => _source != null;
    public bool SuppressClick => Dragging || _cancelled;
    public void Begin(string scope)
    {
        if (_scope != scope || ImGui.IsKeyPressed(ImGuiKey.Escape)) Cancel();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        { _source = null; _cancelled = false; }
        _scope = scope; _targets.Clear(); _append = null;
    }
    public void Register(string id, bool collection)
    {
        if (!_cancelled && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) _source = id;
        if (collection && ImGui.IsItemVisible()) _targets.Add(new(id, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
    }
    public void AppendZone(Vector2 min, Vector2 max) => _append = (min, max);
    public MacroDrop? Finish(float scale)
    {
        MacroDrop? result = null;
        if (_source is { } source && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeAll);
            var blue = ImGui.GetColorU32(new Vector4(.35f, .65f, 1f, .65f));
            var draw = ImGui.GetWindowDrawList();
            var target = _targets.FirstOrDefault(b => ImGui.IsMouseHoveringRect(b.Min, b.Max));
            if (target.Id != null && target.Id != source)
            {
                var after = ImGui.GetMousePos().X >= (target.Min.X + target.Max.X) / 2;
                draw.AddRect(target.Min, target.Max, blue, 4 * scale);
                var x = after ? target.Max.X : target.Min.X;
                draw.AddLine(new(x, target.Min.Y), new(x, target.Max.Y), blue, 2 * scale);
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) result = new(source, target.Id, after);
            }
            else if (_append is { } zone && ImGui.IsMouseHoveringRect(zone.Min, zone.Max))
            {
                draw.AddRect(zone.Min, zone.Max, blue, 4 * scale);
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) result = new(source, null, true);
            }
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) { _source = null; _cancelled = false; }
        return result;
    }
    public void Cancel() { _cancelled |= Dragging; _source = null; }
}
