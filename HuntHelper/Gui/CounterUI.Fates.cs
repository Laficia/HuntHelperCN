using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Fates;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using HuntHelper.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace HuntHelper.Gui;

public unsafe partial class CounterUI : IDisposable
{
    #region Nunni - Fates - Southern Than
    private string _lastFailedFateInfo = string.Empty;
    private readonly int fateRemainingTimeOffset = 0; //changes every patch or something? note to self: this is subtracted from time remaining
    private DateTime _startTime = DateTime.Now;
    private HashSet<IFate> _currentFates = new HashSet<IFate>();

    private readonly Vector4 _green = new Vector4(0, 1, 0, 1);
    private readonly Vector4 _red = new Vector4(1, 0, 0, 1);
    private readonly Vector4 _yellow = new Vector4(1, 1, 0, 1);

    private void DrawNunyunuwiCounter()
    {
        var endTime = _startTime.AddHours(1);

        ImGui.PushFont(UiBuilder.MonoFont);

        DrawNunWindowString(endTime);

        ImGuiUtil.ImGui_Separator(6);

        DrawNunCountdownString(endTime);

        ImGuiUtil.ImGui_Separator(6);

        DrawCurrentFateUI();

        ImGuiUtil.ImGui_Separator(6);

        ImGui.TextColored(_red, _lastFailedFateInfo);

        ImGuiUtil.ImGui_Separator(10);
        DrawNunResetButton();

        ImGui.PopFont();
    }

    private void DrawNunWindowString(DateTime endTime)
    {
        var startTimeString = _startTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        var endTimeString = endTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

        ImGui.Text($"{startTimeString} - {endTimeString}\n");
        ImGui.SameLine();
        Utilities.ImGuiUtil.ImGui_HelpMarker("计时可能因你进入地区前最后一次危命任务失败而提前结束。");
    }

    private void DrawNunCountdownString(DateTime endTime)
    {
        var countdown = endTime.Subtract(DateTime.Now);
        if (countdown.TotalSeconds < 0) countdown = TimeSpan.Zero;
        var countdownString = (countdown).ToString(@"mm\:ss", CultureInfo.InvariantCulture);

        ImGui.Text($"倒计时：");
        ImGui.SameLine();
        if (countdown.TotalSeconds > 300) ImGui.Text($"{countdownString}");
        else ImGui.TextColored(_green, $"{countdownString}");
    }

    private void DrawNunResetButton()
    {
        if (ImGui.Button("重置"))
        {
            _startTime = DateTime.Now;
            _lastFailedFateInfo = string.Empty;
        }
    }

    private void FateUpdateLoop()
    {
        PopulateFateList();
        UpdateCurrentFates();
    }

    private void PopulateFateList()
    {
        foreach (var fate in _fateTable)
        {
            if (_currentFates.Add(fate))
            {
                _currentFates = new HashSet<IFate>(_currentFates.OrderBy(f => f.TimeRemaining > 0 ? f.TimeRemaining : long.MaxValue));
            }
        }

#if DEBUG
        ImGui.PushFont(UiBuilder.MonoFont);
        if (ImGui.CollapsingHeader("危命任务调试信息"))
        {
            ImGui.Text($"{"名称",-35} -> {"时长",4} | " +
                       $"{"剩余",5} | " +
                       $"{"进度",4} : {"状态",12} | " +
                       $"{"开始",10} | " +
                       $"{"编号",4} | " +
                       $"{"位置"}");
            foreach (var fate in _fateTable)
            {
                ImGui.Text($"{fate.Name,-35} -> {fate.Duration,4} | " +
                           $"{new TimeSpan(0, 0, 0, (int)(fate.TimeRemaining - fateRemainingTimeOffset)).ToString(@"mm\:ss"),4} | " +
                           $"{fate.Progress,4} : {FateStateText(fate.State),12} | " +
                           $"{fate.StartTimeEpoch,10} | " +
                           $"{fate.FateId,4} | " +
                           $"{fate.Position}");
            }
        }
        ImGui.PopFont();
#endif
    }

    private void UpdateCurrentFates()
    {
        foreach (var cf in _currentFates)
        {
            if (cf.State == FateState.Ended)
            {
                _currentFates.Remove(cf);
                continue;
            }

            // fate failed or a (!) fate that required [user activation] was not activated in time and disappeared
            if (cf.State == FateState.Failed || (cf.State == FateState.Preparing && _fateTable.All(f => !f.Equals(cf))))
            {
                var failReason = cf.State == FateState.Preparing ? $"失败：未及时开启" : "失败";

                _lastFailedFateInfo = $"{failReason}\n" +
                                      $"{cf.Name} @ {cf.Progress}%%\n\n" +
                                      $"> {DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)} <\n\n" +
                                      $"倒计时已重置。";
                _currentFates.Remove(cf);
                _startTime = DateTime.Now;
                continue;
            }
        }
    }

    private void DrawCurrentFateUI()
    {
        if (ImGui.CollapsingHeader("当前危命任务"))
        {
            foreach (var cf in _currentFates)
            {
                DrawFateInfoUI(cf);
            }
        }
        ImGuiUtil.ImGui_HoveredToolTip("点击危命任务可在地图上查看位置。");
    }
    private void DrawFateInfoUI(IFate cf)
    {
        var fateText = GetFateInfoString(cf);
        var fateTime = GetFateTimeString(cf);

        ImGui.TextUnformatted(fateText);
        if (ImGui.IsItemClicked()) OpenMapOnFateClick(cf);
        Utilities.ImGuiUtil.ImGui_HoveredToolTip($"{cf.Name}");

        ImGui.SameLine();

        switch (cf.TimeRemaining)
        {
            case > 0 and <= 67:
                ImGui.TextColored(_red, fateTime);
                break;
            case > 67 and < 127:
                ImGui.TextColored(_yellow, fateTime);
                break;
            default:
                ImGui.TextUnformatted(fateTime);
                break;
        }
        if (ImGui.IsItemClicked()) OpenMapOnFateClick(cf);
    }

    private static string FateStateText(FateState state) => state.ToString() switch
    {
        "Preparing" => "准备中",
        "Running" => "进行中",
        "Ended" => "已结束",
        "Failed" => "失败",
        _ => $"状态编号 {(int)state}",
    };

    private string GetFateInfoString(IFate fate)
    {
        var fateName = fate.Name.ToString();
        if (fateName.Length > 16) fateName = fateName.Substring(0, 14) + "..";
        return $"{fateName,-16} {fate.Progress,3}%  -> ";
    }

    private string GetFateTimeString(IFate fate)
    {
        return fate.State != FateState.Preparing
            ? $"{new TimeSpan(0, 0, 0, (int)(fate.TimeRemaining - fateRemainingTimeOffset)).ToString(@"mm\:ss", CultureInfo.InvariantCulture)}"
            : "即将开始或需要手动开启；未及时开启可能导致失败。";
    }

    private void OpenMapOnFateClick(IFate fate)
    {
        try
        {
            var ag = AgentMap.Instance();
            var mapId = ag->CurrentMapId;
            var territoryId = ag->CurrentTerritoryId;
            var pos = Dalamud.Utility.MapUtil.WorldToMap(fate.Position, ag->CurrentOffsetX, ag->CurrentOffsetY);

            var mapLink = new MapLinkPayload(territoryId, mapId, pos.X, pos.Y);
            _gameGui.OpenMapWithMapLink(mapLink);
        }
        catch (Exception e)
        {
            PluginLog.Error(e.ToString());
        }
    }
    private void ClientState_TerritoryChanged(uint e)
    {
        _currentFates.Clear();
        _startTime = DateTime.Now;
        _lastFailedFateInfo = string.Empty;

        Task.Run(() =>
        {
            Thread.Sleep(1000);
            return _currentFates =
                new HashSet<IFate>(_currentFates.OrderBy(f =>
                    f.TimeRemaining > 0 ? f.TimeRemaining : Int64.MaxValue));
        });
    }

    #endregion

}
