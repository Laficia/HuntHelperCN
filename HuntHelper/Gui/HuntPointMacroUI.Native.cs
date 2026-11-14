using HuntHelper.Gui.Native;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HuntHelper.Gui;

public sealed partial class HuntPointMacroUI
{
    internal Action? OpenLegacyUi { get; set; }

    internal NativeFormSnapshot NativeSnapshot()
    {
        if (!_open || !NativePresentation) return NativeFormSnapshot.Hidden;
        if (_library.Find(_selected) == null) _selected = "a";
        var entry = _library.Find(_selected)!;
        var draft = _drafts.GetValueOrDefault(_selected, (entry.Name, entry.Text));
        var rows = new List<NativeFormRow>();
        if (_delete != null)
        {
            rows.Add(new("confirm-text", $"删除「{_library.Find(_delete)?.Name}」及其集合按钮？", NativeFormKind.Label));
            rows.Add(new("confirm", "", NativeFormKind.Buttons, Choices: new[] { new NativeFormChoice("delete-confirm", "确认删除"), new NativeFormChoice("delete-cancel", "取消") }));
        }
        rows.Add(new("source", "发送来源", NativeFormKind.Choice, _sourceRank, Choices: new[] { new NativeFormChoice("A", "A 怪狩猎车"), new NativeFormChoice("S", "S 怪记录") }));
        rows.Add(new("target", _target?.Invoke(_sourceRank) ?? "先在 HHT / HHS 选中怪物记录", NativeFormKind.Label));
        rows.Add(new("collection", "宏集合", NativeFormKind.Collection, Enabled: !_busy(),
            Choices: _library.Collection.Select(id => _library.Find(id)).OfType<Utilities.HuntMacroEntry>()
                .Select(e => new NativeFormChoice(e.Id, e.Name, Tooltip: e.Name + "\n" + e.Text + "\n右键编辑或移出集合")).ToArray()));
        rows.Add(new("pages", "", NativeFormKind.Pages, _selected,
            Choices: _library.Entries.Select(e => new NativeFormChoice(e.Id, e.Tab, e.Id == _selected, e.BuiltIn ? "内置预设：可编辑，不能删除" : "右键加入集合或删除")).ToArray()));
        rows.Add(new("name:" + _selected, "缩略名称", NativeFormKind.Input, draft.Item1));
        rows.Add(new("text:" + _selected, "宏正文", NativeFormKind.Multiline, draft.Item2,
            Tooltip: "支持中文输入与多行宏，Enter 换行；编辑不会自动发送。快捷按钮只发送已保存的正文。"));
        rows.Add(new("save-row", "", NativeFormKind.Buttons, Choices: new[] { new NativeFormChoice("save", "保存宏"), new NativeFormChoice("pin-selected", "加入宏集合", Enabled: !_library.Collection.Contains(_selected)) }));
        if (_showVariables) rows.Add(new("variables", "<服务器>/<sever>  <名字>/<name>  <地图>/<map>\n<等级>/<lv>  <分线>/<ins>  <flag>  <wait.数字>（本地等待）", NativeFormKind.Label));
        var toolbar = new List<NativeFormChoice> { new("variables", "变量说明", _showVariables), new("legacy", "原版窗口") };
        if (_busy()) toolbar.Insert(0, new("cancel", "取消发送"));
        return new(true, toolbar, rows, string.Join(" · ", new[] { _status, _sendStatus() }.Where(s => s.Length > 0)));
    }

    internal void NativeDispatch(NativeFormRequest request)
    {
        if (request.Key.StartsWith("name:", StringComparison.Ordinal) || request.Key.StartsWith("text:", StringComparison.Ordinal))
        {
            var id = request.Key[5..]; var entry = _library.Find(id); if (entry == null) return;
            var previous = _drafts.GetValueOrDefault(id, (entry.Name, entry.Text));
            _drafts[id] = request.Key.StartsWith("name:", StringComparison.Ordinal) ? (request.Value, previous.Item2) : (previous.Item1, request.Value);
            return;
        }
        switch (request.Key)
        {
            case "close": _open = false; break;
            case "legacy": OpenLegacyUi?.Invoke(); break;
            case "variables": _showVariables = !_showVariables; break;
            case "source": _sourceRank = request.Value == "S" ? "S" : "A"; break;
            case "pages": case "edit": if (_library.Find(request.Value) != null) _selected = request.Value; break;
            case "add": _selected = _library.Add().Id; _save(); _status = "已新增自定义宏"; break;
            case "save":
                var entry = _library.Find(_selected); if (entry == null) break;
                var draft = _drafts.GetValueOrDefault(_selected, (entry.Name, entry.Text));
                if (_library.Save(_selected, draft.Item1, draft.Item2)) _save();
                _drafts.Remove(_selected); _status = "已保存「" + _library.Find(_selected)!.Name + "」"; break;
            case "send":
                if (_busy()) { _status = "宏正在发送，请稍候或取消"; break; }
                var saved = _library.Find(request.Value);
                if (saved != null && _send != null) { _send(saved.Text, _sourceRank); _status = ""; }
                break;
            case "cancel": _cancel(); break;
            case "pin-selected": if (_library.Pin(_selected)) _save(); break;
            case "pin": if (_library.Pin(request.Value, request.Target.Length == 0 ? null : request.Target, request.After)) _save(); break;
            case "unpin": if (_library.Unpin(request.Value)) _save(); break;
            case "delete": if (_library.Find(request.Value) is { BuiltIn: false }) _delete = request.Value; break;
            case "delete-cancel": _delete = null; break;
            case "delete-confirm":
                if (_delete != null && _library.Delete(_delete)) { _drafts.Remove(_delete); _save(); if (_selected == _delete) _selected = "a"; }
                _delete = null; break;
        }
    }
}
