using Dalamud.Bindings.ImGui;
using HuntHelper.Utilities;
using System.Collections.Generic;
using System.Numerics;

namespace HuntHelper.Gui;

/// <summary>Shared server/version tab drag: blue outline, horizontal cursor, no native yellow target.</summary>
internal sealed class TabDragGesture(bool grabOnHold = false)
{
    private const double HoldSeconds = .35;
    private uint? _source;
    private uint? _pressed;
    private double _pressedAt;
    private bool _cancelled;
    public bool Dragging => _source.HasValue;
    public bool SuppressClick => Dragging || _cancelled ||
        (grabOnHold && _pressed.HasValue && ImGui.GetTime() - _pressedAt >= HoldSeconds);
    public void RegisterItem(uint id)
    {
        if (!_cancelled && ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            if (_pressed != id) { _pressed = id; _pressedAt = ImGui.GetTime(); }
            if (ImGui.GetTime() - _pressedAt >= HoldSeconds && (grabOnHold || ImGui.IsMouseDragging(ImGuiMouseButton.Left))) _source = id;
        }
    }

    public bool Finish(List<uint> order, IEnumerable<(uint Id, Vector2 Min, Vector2 Max)> tabs, float scale)
    {
        var changed = false;
        if (_source is { } source)
        {
            ImGui.SetMouseCursor(grabOnHold ? ImGuiMouseCursor.ResizeAll : ImGuiMouseCursor.ResizeEw);
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
                foreach (var target in tabs)
                {
                    if (target.Id == source || !ImGui.IsMouseHoveringRect(target.Min, target.Max)) continue;
                    ImGui.GetWindowDrawList().AddRect(target.Min, target.Max,
                        TrainTheme.DragColor(.65f), 4 * scale);
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        changed |= WorldTabOrder.MoveRelative(order, source, target.Id,
                            ImGui.GetMousePos().X >= (target.Min.X + target.Max.X) / 2);
                }
        }
        EndFrame();
        return changed;
    }

    public void Cancel()
    {
        _cancelled |= _pressed.HasValue;
        _source = null;
    }

    public void EndFrame()
    {
        if (ImGui.IsMouseDown(ImGuiMouseButton.Left)) return;
        _source = _pressed = null;
        _cancelled = false;
    }
}
