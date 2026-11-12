using Dalamud.Bindings.ImGui;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HuntHelper.Gui;

public sealed class HuntPointMacroUI
{
    private readonly HuntMacroLibrary _library;
    private readonly Action _save, _cancel;
    private readonly Func<bool> _busy;
    private readonly Func<string> _sendStatus;
    private readonly Action<string, string>? _send;
    private readonly Func<string, string>? _target;
    private readonly MacroCollectionDrag _drag = new();
    private readonly Dictionary<string, (string Name, string Text)> _drafts = new();
    private bool _open;
    private string _selected = "a", _sourceRank = "A", _status = "";
    private string? _delete;
    internal Dictionary<string, (Vector2 Min, Vector2 Max)> ItemBounds { get; } = new();

    public HuntPointMacroUI(HuntPointMacroSettings settings, Action save, Func<bool> busy, Func<string> status,
        Action cancel, Action<string, string>? send = null, Func<string, string>? target = null)
    {
        _library = new(settings); _save = save; _busy = busy; _sendStatus = status; _cancel = cancel;
        _send = send; _target = target;
        if (_library.Repair()) _save();
    }
    public void Open(string rank = "A")
    {
        _sourceRank = rank == "S" ? "S" : "A"; _selected = _sourceRank == "S" ? "s" : "a";
        _status = ""; _open = true; _drag.Cancel();
    }
    public void Draw()
    {
        if (!_open) { _drag.Cancel(); return; }
        var scale = ImGui.GetFontSize() / 14f;
        ImGui.SetNextWindowSize(new Vector2(700, 620), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(300, 330), new Vector2(float.MaxValue));
        if (ImGui.Begin("预设宏##HuntPointMacros", ref _open))
        {
            ItemBounds.Clear();
            var entries = _library.Entries;
            if (_library.Find(_selected) == null) _selected = "a";
            _drag.Begin(_sourceRank + ":" + string.Join(",", entries.Select(e => e.Id)) + ":" + string.Join(",", _library.Collection));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4 * scale);
            DrawCollection(scale);
            ImGui.TextWrapped(_target?.Invoke(_sourceRank) ?? "使用当前选中怪物：请先在 HHT / HHS 选中记录");
            ImGui.Separator();
            DrawTabs(entries, scale);
            ImGui.Separator();
            DrawEditor(scale);
            DrawDeleteModal();
            if (_drag.Finish(scale) is { } drop && _library.Pin(drop.Id, drop.Target, drop.After))
            { _save(); _status = "宏集合已保存；点击发送，按住拖动排序。"; }
            ImGui.PopStyleVar();
        }
        else _drag.Cancel();
        ImGui.End();
    }

    private void DrawCollection(float scale)
    {
        ImGui.TextUnformatted("宏集合");
        if (ImGui.GetContentRegionAvail().X > ImGui.CalcTextSize("宏集合 点击发送 · 按住拖动排序").X) ImGui.SameLine();
        MutedWrapped("点击发送 · 按住拖动排序");
        var available = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)((available + spacing) / (145 * scale + spacing)));
        var width = Math.Min(175 * scale, (available - spacing * (columns - 1)) / columns);
        var n = 0;
        foreach (var id in _library.Collection.ToArray())
        {
            var entry = _library.Find(id); if (entry == null) continue;
            if (n++ % columns != 0) ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(.13f, .18f, .25f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(.19f, .3f, .43f, 1));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(.23f, .41f, .62f, 1));
            var clicked = ImGui.Button($"##MacroCollection-{id}", new Vector2(width, 55 * scale));
            ImGui.PopStyleColor(3);
            Remember("collection:" + id); _drag.Register(id, true);
            var min = ImGui.GetItemRectMin(); var max = ImGui.GetItemRectMax();
            var draw = ImGui.GetWindowDrawList();
            draw.AddText(min + new Vector2(9, 6) * scale, ImGui.GetColorU32(ImGuiCol.TextDisabled), "::");
            draw.AddText(min + new Vector2(24, 6) * scale, ImGui.GetColorU32(ImGuiCol.Text), Fit(entry.Name, width - 50 * scale));
            draw.AddText(new(max.X - 17 * scale, min.Y + 6 * scale), ImGui.GetColorU32(ImGuiCol.TextDisabled), ">");
            draw.AddText(min + new Vector2(9, 30) * scale, ImGui.GetColorU32(new Vector4(.72f, .78f, .86f, 1)), Fit(FirstLine(entry.Text), width - 18 * scale));
            if (clicked && !_drag.SuppressClick)
            {
                if (_busy()) _status = "坐标宏正在发送，请稍候或取消剩余发送。";
                else if (_send != null) { _send(entry.Text, _sourceRank); _status = ""; }
                else _status = "发送接口尚未就绪";
            }
            if (ImGui.IsItemHovered() && !_drag.Dragging) ImGui.SetTooltip($"{entry.Name}\n{entry.Text}\n点击发送；右键编辑或移出集合");
            if (ImGui.BeginPopupContextItem("MacroCollectionMenu-" + id))
            {
                if (ImGui.MenuItem("编辑")) _selected = id;
                if (ImGui.MenuItem("从集合移除") && _library.Unpin(id)) { _save(); _drag.Cancel(); }
                ImGui.EndPopup();
            }
        }
        ImGui.Dummy(new Vector2(available, 28 * scale));
        Remember("drop");
        var lo = ImGui.GetItemRectMin(); var hi = ImGui.GetItemRectMax();
        _drag.AppendZone(lo, hi);
        var list = ImGui.GetWindowDrawList();
        list.AddRect(lo, hi, ImGui.GetColorU32(ImGuiCol.Border), 4 * scale);
        var label = "+ 拖入下方宏标签";
        list.AddText((lo + hi - ImGui.CalcTextSize(label)) / 2, ImGui.GetColorU32(ImGuiCol.TextDisabled), label);
        ImGui.Spacing();
    }

    private void DrawTabs(IReadOnlyList<HuntMacroEntry> entries, float scale)
    {
        var startX = ImGui.GetCursorPosX();
        var right = ImGui.GetWindowContentRegionMax().X;
        var first = true;
        foreach (var entry in entries)
        {
            var width = Math.Min(190 * scale, ImGui.CalcTextSize(entry.Tab).X + ImGui.GetStyle().FramePadding.X * 2);
            if (!first && ImGui.GetCursorPosX() + ImGui.GetItemRectSize().X + width + ImGui.GetStyle().ItemSpacing.X <= right)
                ImGui.SameLine();
            else if (!first) ImGui.SetCursorPosX(startX);
            var selected = entry.Id == _selected;
            ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(selected ? ImGuiCol.ButtonActive : ImGuiCol.Button));
            var clicked = ImGui.Button(Fit(entry.Tab, width - ImGui.GetStyle().FramePadding.X * 2) + "###MacroPage-" + entry.Id,
                new Vector2(width, ImGui.GetFrameHeight() + 4 * scale));
            ImGui.PopStyleColor();
            Remember("tab:" + entry.Id); _drag.Register(entry.Id, false);
            if (clicked && !_drag.SuppressClick) _selected = entry.Id;
            if (ImGui.BeginPopupContextItem("MacroPageMenu-" + entry.Id))
            {
                if (ImGui.MenuItem("加入宏集合", enabled: !_library.Collection.Contains(entry.Id)) && _library.Pin(entry.Id)) _save();
                if (ImGui.MenuItem("从集合移除", enabled: _library.Collection.Contains(entry.Id)) && _library.Unpin(entry.Id)) _save();
                if (!entry.BuiltIn && ImGui.MenuItem("删除自定义宏")) _delete = entry.Id;
                ImGui.EndPopup();
            }
            first = false;
        }
        var addWidth = ImGui.CalcTextSize("+").X + ImGui.GetStyle().FramePadding.X * 2;
        if (ImGui.GetCursorPosX() + ImGui.GetItemRectSize().X + addWidth + ImGui.GetStyle().ItemSpacing.X <= right) ImGui.SameLine();
        if (ImGui.Button("+##AddMacroPage", new Vector2(0, ImGui.GetFrameHeight() + 4 * scale))) { _selected = _library.Add().Id; _save(); _drag.Cancel(); _status = "已新增自定义宏；可修改缩略名称和正文。"; }
        Remember("add");
    }

    private void DrawEditor(float scale)
    {
        var entry = _library.Find(_selected)!;
        var draft = _drafts.GetValueOrDefault(entry.Id, (entry.Name, entry.Text));
        ImGui.TextUnformatted("缩略名称");
        var available = ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(); ImGui.SetNextItemWidth(Math.Min(180 * scale, Math.Max(80 * scale, available - 95 * scale)));
        var name = draft.Item1;
        ImGui.InputText("##MacroShortName", ref name, 180); Remember("name");
        var binding = entry.Id switch { "a" => "用于 HHT 的“坐标”按钮", "s" => "用于 HHS 的“坐标”按钮", _ => "自定义宏" };
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(binding).X <
            ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X) ImGui.SameLine();
        MutedWrapped(binding);
        var text = draft.Item2;
        ImGui.InputTextMultiline("##MacroTemplate", ref text, 6000, new Vector2(-1, 112 * scale)); Remember("text");
        _drafts[entry.Id] = (name, text);
        ImGui.TextWrapped("<服务器> / <sever>   <名字> / <name>   <地图> / <map>   <等级> / <lv>   <分线> / <ins>   <flag>");
        if (ImGui.Button("保存宏"))
        {
            if (_library.Save(entry.Id, name, text)) _save();
            _drafts.Remove(entry.Id); _status = "已保存「" + _library.Find(entry.Id)!.Name + "」；编辑不会自动发送。";
        }
        Remember("save");
        if (ImGui.GetContentRegionAvail().X > 260 * scale) ImGui.SameLine();
        MutedWrapped("拖动标签到上方，可加入宏集合");
        if (_busy() && ImGui.Button("取消剩余发送")) _cancel();
        if (_status.Length > 0) ImGui.TextWrapped(_status);
        var sendStatus = _sendStatus(); if (sendStatus.Length > 0) ImGui.TextWrapped(sendStatus);
        MutedWrapped("/echo 仅自己可见；快捷发送使用已保存的宏。");
    }

    private void DrawDeleteModal()
    {
        if (_delete == null) return;
        ImGui.OpenPopup("删除自定义宏##ConfirmMacroDelete");
        if (ImGui.BeginPopupModal("删除自定义宏##ConfirmMacroDelete", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("删除「" + _library.Find(_delete)?.Name + "」及其集合按钮？");
            if (ImGui.Button("删除"))
            {
                if (_library.Delete(_delete)) _save();
                _drafts.Remove(_delete); if (_selected == _delete) _selected = "a";
                _delete = null; _drag.Cancel(); ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button("取消")) { _delete = null; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
    }
    private void Remember(string key) => ItemBounds[key] = (ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
    private static void MutedWrapped(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        ImGui.TextWrapped(text); ImGui.PopStyleColor();
    }
    private static string FirstLine(string text) => text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "（空宏）";
    private static string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) return text;
        var length = text.Length;
        while (length > 0 && ImGui.CalcTextSize(text[..length] + "…").X > width)
        { length--; if (length > 0 && char.IsHighSurrogate(text[length - 1])) length--; }
        return text[..length] + "…";
    }
}
