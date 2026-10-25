using Dalamud.Bindings.ImGui;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

/// <summary>Name-cell drag with the same blue feedback as server/version tabs.</summary>
internal sealed class HuntRowDragGesture<T> where T : class
{
    private T? _source;
    private bool _cancelled;
    private string? _scope;
    private List<T> _rows = new();
    private readonly List<(T Row, Vector2 Min, Vector2 Max)> _bounds = new();
    internal bool Dragging => _source != null;

    public void Begin(string scope, IReadOnlyList<T> rows)
    {
        // A scan, deletion, restore or tab change must never retarget an ongoing drag.
        if (_scope != scope || !_rows.SequenceEqual(rows) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            Cancel();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseReleased(ImGuiMouseButton.Left))
        { _source = null; _cancelled = false; }
        _scope = scope;
        _rows = rows.ToList();
        _bounds.Clear();
    }

    public bool Selectable(T row, string label, bool selected)
    {
        var clicked = ImGui.Selectable(label, selected);
        if (!_cancelled && ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) _source = row;
        if (ImGui.IsItemVisible()) _bounds.Add((row, ImGui.GetItemRectMin(), ImGui.GetItemRectMax()));
        return clicked && !Dragging && !_cancelled;
    }

    public List<T>? Finish(float scale)
    {
        // Hit testing and draw commands must use the name column's clip rectangle,
        // rather than the last action/death column rendered by the caller.
        ImGui.TableSetColumnIndex(0);
        List<T>? result = null;
        if (_source is { } source && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNs);
            foreach (var target in _bounds)
            {
                if (ReferenceEquals(source, target.Row) || !ImGui.IsMouseHoveringRect(target.Min, target.Max)) continue;
                var after = ImGui.GetMousePos().Y >= (target.Min.Y + target.Max.Y) / 2;
                var blue = ImGui.GetColorU32(new Vector4(.35f, .65f, 1f, .65f));
                var draw = ImGui.GetWindowDrawList();
                draw.AddRect(target.Min, target.Max, blue, 4 * scale);
                var y = after ? target.Max.Y : target.Min.Y;
                draw.AddLine(new(target.Min.X, y), new(target.Max.X, y), blue, 2 * scale);
                if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                {
                    var order = _rows.ToList();
                    order.Remove(source);
                    order.Insert(order.IndexOf(target.Row) + (after ? 1 : 0), source);
                    if (!order.SequenceEqual(_rows)) result = order;
                }
                break;
            }
            // Scroll the table while holding a record near its top/bottom edge.
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                var mouseY = ImGui.GetMousePos().Y;
                var top = ImGui.GetWindowPos().Y + 48 * scale;
                var bottom = ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - 24 * scale;
                if (mouseY < top) ImGui.SetScrollY(ImGui.GetScrollY() - 360 * scale * ImGui.GetIO().DeltaTime);
                else if (mouseY > bottom) ImGui.SetScrollY(ImGui.GetScrollY() + 360 * scale * ImGui.GetIO().DeltaTime);
            }
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) { _source = null; _cancelled = false; }
        return result;
    }

    public void Cancel() { _cancelled |= Dragging; _source = null; }
}
