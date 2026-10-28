using JinYong.Core.Battles;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Projects existing battle queries into display data without advancing rules or RNG.</summary>
public static class BattleActionModelFactory
{
    public static BattleActionModel Build(GameSession session, BattleSession battle, BattleActionToken token,
        string? error = null, string targetHint = "移动后仍可行动。直线武功按所点方向出招。")
    {
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(battle); ArgumentNullException.ThrowIfNull(token);
        var selecting = battle.Phase == BattlePhase.PartySelection;
        var presenting = battle.IsPresenting;
        var friendly = battle.CurrentActor is { IsFriendly: true };
        var canAct = battle.Phase == BattlePhase.Acting && friendly && !presenting;
        var partyOptions = selecting ? battle.SelectableRoleIds.Select(id => new BattlePartyOption(id,
            session.Party!.Roles[id].Name, battle.MandatoryRoleIds.Contains(id))).ToArray() : Array.Empty<BattlePartyOption>();
        var magicOptions = Array.Empty<BattleMagicOption>();
        var itemOptions = Array.Empty<BattleItemOption>();
        if (canAct)
        {
            var actor = battle.CurrentActor!;
            magicOptions = battle.AvailableMagicSlots().Select(slot =>
            {
                var id = actor.Role[RoleWords.MagicIdBegin + slot];
                return new BattleMagicOption(slot, session.BattleResources!.Magics[id].Name);
            }).ToArray();
            itemOptions = battle.AvailableBattleItems().Select(id => new BattleItemOption(id,
                session.BattleResources!.Items[id].Name + (session.Inventory.TryGetValue(id, out var quantity) ? $" × {quantity}" : ""))).ToArray();
        }
        return new(token: token, status: Status(battle, error), isSelecting: selecting,
            finished: battle.Phase == BattlePhase.Finished, present: presenting,
            waitForConfirmation: battle.Presentation is { WaitingForConfirmation: true }, isFriendlyTurn: friendly,
            partyOptions: partyOptions, magicOptions: magicOptions, itemOptions: itemOptions,
            canMove: canAct && battle.ReachableMoves().Count > 1,
            canPoison: canAct && battle.CanUseSupport(BattleSupportAction.UsePoison),
            canDetox: canAct && battle.CanUseSupport(BattleSupportAction.Detox),
            canMedicine: canAct && battle.CanUseSupport(BattleSupportAction.Medicine), targetHint: targetHint,
            selectedMagicSlot: magicOptions.FirstOrDefault()?.Slot ?? -1, selectedItemId: itemOptions.FirstOrDefault()?.Id ?? -1);
    }

    public static string Status(BattleSession battle, string? error = null)
    {
        ArgumentNullException.ThrowIfNull(battle);
        var actor = battle.CurrentActor;
        var visible = battle.Presentation?.Combatants.FirstOrDefault(person => person.Slot == actor?.Slot);
        var status = actor is null ? "请选择出战人物。" :
            $"第 {battle.Round} 回合 · {(actor.IsFriendly ? "我方" : "敌方")}\n{actor.Name}\n生命 {visible?.Hp ?? actor.Hp}/{visible?.MaximumHp ?? actor.MaximumHp}\n内力 {visible?.Mp ?? actor.Mp} · 体力 {visible?.Words[RoleWords.PhysicalPower] ?? actor.PhysicalPower}\n伤势 {visible?.Hurt ?? actor.Role[RoleWords.Hurt]} · 中毒 {visible?.Poison ?? actor.Role[RoleWords.Poison]}\n剩余移动 {actor.RoundValue}";
        if (battle.Phase == BattlePhase.Finished) status = battle.Outcome == BattleOutcome.Victory ? "战斗胜利" : "战斗落败";
        if (error is not null) status += "\n" + error;
        return status;
    }
}
