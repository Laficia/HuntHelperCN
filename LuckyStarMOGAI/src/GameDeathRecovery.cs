using System;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace LuckyStar;

internal sealed unsafe class GameDeathRecovery
{
    private long nextOpenPrompt;

    public void Reset() => nextOpenPrompt = 0;

    public bool TryReturnHome()
    {
        if (Svc.Objects.LocalPlayer is not { CurrentHp: 0 } || Svc.Condition[ConditionFlag.BoundByDuty]) return false;
        var agent = AgentRevive.Instance();
        if (agent == null || agent->Revive == null || agent->Revive->State != ReviveState.Revivable) return false;

        var addon = Svc.GameGui.GetAddonByName("SelectYesno", 1);
        if (!addon.IsVisible)
        {
            // Reopen the game's own Return Confirmation notice if it is minimized to the HUD.
            if (Environment.TickCount64 >= nextOpenPrompt && !agent->IsAddonShown())
            {
                nextOpenPrompt = Environment.TickCount64 + 2_000;
                agent->ShowAddon();
            }
            return false;
        }

        var dialog = (AddonSelectYesno*)addon.Address;
        if (dialog == null || !dialog->IsReady || dialog->PromptText == null) return false;
        var text = new AddonMaster.SelectYesno(dialog).Text;
        // CN Addon sheet row 111. Match the question itself so other Yes/No windows and raises are ignored.
        if (!text.Contains("要传送到返回点吗", StringComparison.Ordinal) ||
            !text.Contains("当前返回点", StringComparison.Ordinal)) return false;

        Callback.Fire(&dialog->AtkUnitBase, true, 0);
        return true;
    }

    public string? TryTeleport(uint territory)
    {
        var crystal = Svc.AetheryteList
            .Where(entry => entry.TerritoryId == territory && entry.SubIndex == 0 && entry.AetheryteData.ValueNullable is { IsAetheryte: true })
            .Where(entry => territory != 613 || entry.AetheryteId == 105) // Underwater return via Tamamizu.
            .OrderBy(entry => entry.GilCost)
            .ThenBy(entry => entry.AetheryteId)
            .FirstOrDefault();
        if (crystal == null) return territory == 613 ? "未解锁红玉海玉水村水晶，农怪已停止" : "原地图没有已解锁的传送水晶，农怪已停止";
        var telepo = Telepo.Instance();
        return telepo != null && telepo->Teleport(crystal.AetheryteId, crystal.SubIndex)
            ? null : "游戏未接受传送请求，农怪已停止";
    }
}
