using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Component.GUI;
using KamiToolKit.Nodes;

namespace HuntHelper.Gui.Native;

/// <summary>A fixed native confirmation; no scrolling or record mutation in event handlers.</summary>
internal sealed class NativeClearWindow(Action cancel, Action<string> confirm, Func<bool> canInteract)
    : NativePersistentAddon(cancel, canInteract)
{
    private NativeClearSnapshot? snapshot;
    private TextNode? scope, count, boundary, recovery;
    private TextButtonNode? accept, reject;

    internal void Apply(NativeClearSnapshot value)
    { snapshot = value; if (accept != null) Layout(); }

    protected override unsafe void OnSetup(AtkUnitBase* addon, Span<AtkValue> values)
    {
        base.OnSetup(addon, values);
        scope = Label(); count = Label(); boundary = Label(); recovery = Label();
        accept = new TextButtonNode { Size = new(152, 28), OnClick = () => {
            if (snapshot is not { Count: > 0 } current) return;
            confirm(current.Token); CloseFromModel();
        } };
        accept.LabelNode.FontSize = 13; accept.AttachNode(this);
        reject = new TextButtonNode { String = "取消", Size = new(88, 28), OnClick = Close };
        reject.LabelNode.FontSize = 13; reject.AttachNode(this);
        Layout();
    }

    private TextNode Label()
    {
        var label = new TextNode { FontSize = 13, TextColorID = 8, Size = new(410, 24) };
        label.AddTextFlags(TextFlags.Ellipsis); label.AttachNode(this); return label;
    }

    private void Layout()
    {
        if (snapshot == null || accept == null) return;
        var start = ContentStartPosition;
        var labels = new[] { scope!, count!, boundary!, recovery! };
        for (var i = 0; i < labels.Length; i++)
        { labels[i].Position = start + new Vector2(2, i * 26); labels[i].Size = new(Size.X - start.X * 2 - 4, 24); }
        scope!.String = "清理范围：" + snapshot.Scope; scope.TextTooltip = scope.String;
        count!.String = $"共 {snapshot.Count} 条{snapshot.Rank}怪记录，涉及 {snapshot.WorldCount} 个服务器。";
        boundary!.String = snapshot.AllWorlds ? "清除当前模块的全部服线；A/S 记录分开。" : "仅清空当前标签，其他服务器和版本保留。";
        recovery!.String = "删除前自动备份，可在「备份撤销」中恢复。";
        accept.String = snapshot.AllWorlds ? "确认清除全部" : "确认清空";
        accept.IsEnabled = snapshot.Count > 0;
        accept.Position = start + new Vector2(2, 116);
        reject!.Position = start + new Vector2(164, 116);
    }

    protected override unsafe void OnFinalize(AtkUnitBase* addon)
    { scope = count = boundary = recovery = null; accept = reject = null; base.OnFinalize(addon); }
}
