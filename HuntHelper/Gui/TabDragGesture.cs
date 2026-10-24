using Dalamud.Bindings.ImGui;
using HuntHelper.Utilities;
using System.Collections.Generic;
using System.Numerics;

namespace HuntHelper.Gui;

/// <summary>Shared server/version tab drag: blue outline, horizontal cursor, no native yellow target.</summary>
internal sealed class TabDragGesture
{
    private uint? _source;
    public bool Dragging => _source.HasValue;
    public void RegisterItem(uint id)
    {
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left)) _source = id;
    }

    public bool Finish(List<uint> order, IEnumerable<(uint Id, Vector2 Min, Vector2 Max)> tabs, float scale)
    {
        var changed = false;
        if (_source is { } source)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEw);
            if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
                foreach (var target in tabs)
                {
                    if (target.Id == source || !ImGui.IsMouseHoveringRect(target.Min, target.Max)) continue;
                    ImGui.GetWindowDrawList().AddRect(target.Min, target.Max,
                        ImGui.GetColorU32(new Vector4(.35f, .65f, 1f, .65f)), 4 * scale);
                    if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
                        changed |= WorldTabOrder.MoveRelative(order, source, target.Id,
                            ImGui.GetMousePos().X >= (target.Min.X + target.Max.X) / 2);
                }
        }
        EndFrame();
        return changed;
    }

    public void EndFrame() { if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _source = null; }
}
