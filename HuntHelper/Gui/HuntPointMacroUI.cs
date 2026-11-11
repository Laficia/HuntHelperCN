using Dalamud.Bindings.ImGui;
using HuntHelper.Utilities;
using System;
using System.Numerics;

namespace HuntHelper.Gui;

public sealed class HuntPointMacroUI
{
    private readonly HuntPointMacroSettings _settings;
    private readonly Action _save, _cancel;
    private readonly Func<bool> _busy;
    private readonly Func<string> _sendStatus;
    private bool _open;
    private string? _selectRank;
    private string _a = "", _s = "", _status = "";
    public HuntPointMacroUI(HuntPointMacroSettings settings, Action save, Func<bool> busy, Func<string> status, Action cancel)
    { _settings = settings; _save = save; _busy = busy; _sendStatus = status; _cancel = cancel; }
    public void Open(string rank = "A") { _selectRank = rank; _a = _settings.A; _s = _settings.S; _status = ""; _open = true; }
    public void Draw()
    {
        if (!_open) return;
        ImGui.SetNextWindowSize(new Vector2(620, 400), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("预设宏##HuntPointMacros", ref _open))
        {
            ImGui.TextWrapped("A、S 坐标按钮分别使用下面对应预设。编辑与保存不会发送消息。");
            if (ImGui.BeginTabBar("MacroRanks"))
            {
                DrawRank("A预设", ref _a, false, _selectRank == "A");
                DrawRank("S预设", ref _s, true, _selectRank == "S");
                _selectRank = null;
                ImGui.EndTabBar();
            }
            ImGui.TextWrapped("<flag>：该记录的可点击地图点位；<服务器>：记录所在服，同当前所在服时为“本地”。");
            ImGui.TextWrapped("其他变量：<名字>、<等级>、<地图>、<分线>、<x>、<y>。每行以 /p、/sh、/echo 等聊天指令开头，最多15行，间隔1.2秒发送。");
            if (ImGui.Button("保存 A / S 预设"))
            {
                _settings.A = _a; _settings.S = _s; _save(); _status = "已保存，点击记录旁的“坐标”发送。";
            }
            if (_busy()) { ImGui.SameLine(); if (ImGui.Button("取消剩余发送")) _cancel(); }
            ImGui.TextWrapped(_status);
            ImGui.TextWrapped(_sendStatus());
        }
        ImGui.End();
    }

    private static void DrawRank(string label, ref string text, bool s, bool select)
    {
        if (!ImGui.BeginTabItem(label, select ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None)) return;
        ImGui.TextWrapped(s ? "用于 /hhs S怪的“坐标”按钮" : "用于 /hht A怪的“坐标”按钮");
        if (ImGui.Button("队伍预设")) text = s ? "/p 下一站【<服务器>】S怪<flag>，请等待触发倒计时/定，不要抢开！" : "/p 下一站【<服务器>】<名字><flag>";
        ImGui.SameLine();
        if (ImGui.Button("喊话预设")) text = s ? new Utilities.HuntPointMacroSettings().S : "/sh 下一站【<服务器>】<名字><flag>";
        ImGui.SameLine();
        if (ImGui.Button("本地回显预设")) text = "/echo 【<服务器>】<等级>怪 <名字><flag><分线>";
        ImGui.InputTextMultiline("##Template", ref text, 6000, new Vector2(-1, 130));
        ImGui.EndTabItem();
    }
}
