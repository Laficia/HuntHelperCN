using Dalamud.Bindings.ImGui;
using System;
using System.Numerics;

namespace HuntHelper.Gui;

internal enum TrainExportScope { Version, Server, AllServers }

internal sealed class TrainExportWindow(string rank)
{
    private readonly string _title = $"{(rank == "S" ? "S 怪记录" : "狩猎车")} · 导出###HHTTrainExport{rank}";
    private bool _open;
    private bool _focus;
    private string? _code;
    private string _status = string.Empty;

    internal bool IsOpen => _open;

    internal void Open()
    {
        _open = _focus = true;
        _code = null;
        _status = string.Empty;
    }

    internal void Draw(string version, Func<TrainExportScope, string?> createCode, Func<string, bool> copyCode)
    {
        var viewport = ImGui.GetMainViewport();
        if (_focus)
        {
            ImGui.SetNextWindowFocus();
            ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * .5f, ImGuiCond.Always, new Vector2(.5f));
            _focus = false;
        }

        const ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings;
        if (ImGui.Begin(_title, ref _open, flags))
        {
            var width = MathF.Max(1, MathF.Min(ImGui.GetFontSize() * 23, viewport.WorkSize.X - ImGui.GetStyle().WindowPadding.X * 4));
            DrawScope($"当前标签（{version}）###ExportVersion", TrainExportScope.Version);
            DrawScope("当前服务器###ExportServer", TrainExportScope.Server);
            DrawScope("全部服务器###ExportAllServers", TrainExportScope.AllServers);
            if (_status.Length > 0)
            {
                ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);
                ImGui.TextUnformatted(_status);
                ImGui.PopTextWrapPos();
            }
            if (_code is { } code)
            {
                ImGui.SetNextItemWidth(width);
                ImGui.InputText("##ExportCode", ref code, code.Length + 1, ImGuiInputTextFlags.ReadOnly | ImGuiInputTextFlags.AutoSelectAll);
                if (ImGui.Button("重新复制")) Copy(code);
            }

            void DrawScope(string label, TrainExportScope scope)
            {
                if (!ImGui.Button(label, new Vector2(width, 0))) return;
                _code = createCode(scope);
                if (_code is { } result) Copy(result);
                else _status = "这个范围没有可导出的记录。";
            }

            void Copy(string value) => _status = copyCode(value)
                ? "导出代码已复制到剪贴板。"
                : "导出代码已生成。剪贴板写入未确认，可重新复制或选中下方代码手动复制。";
        }
        ImGui.End();
        if (!_open) _code = null;
    }
}
