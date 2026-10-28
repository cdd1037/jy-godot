using JinYong.Core.Battles;
using JinYong.Core.Events;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    // Normal DOS startup zeroes the BSS range containing word_E6EC2.
    private int _battleExperienceFlag, _battleHpCostScale;
    public BattleCatalog? BattleResources { get; }
    public BattleSession? Battle { get; private set; }
    public BattleOutcome? LastBattleOutcome { get; private set; }
    public BattleSettlementResult? LastBattleSettlement { get; private set; }

    private void ValidateBattleCommand(EventCommand command)
    {
        if (command.Operation != EventOperation.Battle) return;
        if (Party is null || BattleResources is null || !BattleResources.Definitions.ContainsKey(command.Arguments[0]))
            throw new InvalidOperationException("战斗资料未导入或编号不存在，请重新导入完整资源库。");
        if (BattleResources.Progression is null)
            throw new InvalidOperationException("原版经验与修炼规则未导入。");
    }

    void IEventWorld.BeginBattle(int battleId, int experienceFlag)
    {
        if (Battle is not null || IsWorld || PendingTravel is not null) throw new InvalidOperationException("A battle cannot start in the current mode.");
        var resources = BattleResources ?? throw new InvalidOperationException("Battle resources are unavailable.");
        var definition = resources.Definitions[battleId];
        var candidate = new BattleSession(definition, resources.Fields[definition.BattlefieldId], Party!, resources.Magics,
            resources.Items, initialCostScale: _battleHpCostScale, random: Events!.DrawRandom, inventory: this, presentActions: true);
        _battleExperienceFlag = experienceFlag;
        Battle = candidate; LastBattleOutcome = null;
        LastBattleSettlement = null;
        PresentationRevision++;
    }

    public bool FinishBattle()
    {
        if (Battle is not { Outcome: not BattleOutcome.Ongoing, IsPresenting: false } battle || Events?.PendingBattle is not { } pending) return false;
        // Settlement is performed before consuming the continuation, so repeating
        // an old UI confirmation cannot pay rewards twice or choose a branch.
        SettleBattle(battle, _battleExperienceFlag);
        battle.CompleteAfterSettlement();
        _battleHpCostScale = battle.HpCostScale;
        LastBattleOutcome = battle.Outcome;
        Battle = null;
        PresentationRevision++;
        return Events.ResumeBattle(pending.Token);
    }

    bool IEventWorld.TryGetBattleOutcome(out bool victory)
    {
        victory = LastBattleOutcome == BattleOutcome.Victory;
        return Battle is null && LastBattleOutcome.HasValue;
    }

    private void SettleBattle(BattleSession battle, int experienceFlag)
    {
        var resources = BattleResources!;
        LastBattleSettlement = BattleSettlement.Settle(battle.Definition, battle.Combatants,
            battle.Outcome == BattleOutcome.Victory, experienceFlag, resources.Progression!, resources.Items,
            Events!.DrawRandom, _inventory, _itemIds);
    }
}
