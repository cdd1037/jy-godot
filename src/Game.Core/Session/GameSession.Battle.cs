using JinYong.Core.Battles;
using JinYong.Core.Events;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    // Normal DOS startup zeroes the BSS range containing word_E6EC2.
    private int _battleExperienceFlag, _battleHpCostScale;
    public BattleCatalog? BattleResources { get; }
    private BattleSession? _activeBattle;
    public BattleSession? Battle
    {
        get => _activeBattle;
        private set
        {
            _activeBattle = value;
            // This also covers RetireForReplacement's existing Battle=null path.
            _settlementBattle = null; _settlementToken = 0; _settlementAttempted = false;
            _pendingBattleSettlement = null; _battleSettlementError = null;
        }
    }
    public BattleOutcome? LastBattleOutcome { get; private set; }
    public BattleSettlementResult? LastBattleSettlement { get; private set; }
    private BattleSession? _settlementBattle;
    private long _settlementToken;
    private bool _settlementAttempted;
    private BattleSettlementResult? _pendingBattleSettlement;
    private string? _battleSettlementError;
    private bool HasCurrentSettlement => !IsRetired && Battle is not null && ReferenceEquals(Battle, _settlementBattle) &&
        Events?.PendingBattle?.Token == _settlementToken;
    /// <summary>Frozen, already committed rewards while the original battlefield/event token await final confirmation.</summary>
    public BattleSettlementResult? PendingBattleSettlement => HasCurrentSettlement ? _pendingBattleSettlement : null;
    /// <summary>Calculation failure for this battle. A failed preparation is not retried or charged RNG again.</summary>
    public string? BattleSettlementError => HasCurrentSettlement ? _battleSettlementError : null;

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

    /// <summary>
    /// Commits settlement once at a finished, unpresented boundary and retains the battle continuation.
    /// Not ready returns false without an error. Calculation failure returns false with BattleSettlementError;
    /// subsequent calls return the cached success/failure without rewards or further RNG consumption.
    /// </summary>
    public bool PrepareBattleSettlement()
    {
        if (IsRetired || Battle is not { Phase: BattlePhase.Finished, Outcome: not BattleOutcome.Ongoing, IsPresenting: false } battle ||
            Events?.PendingBattle is not { } pending) return false;
        if (HasCurrentSettlement && _settlementAttempted) return _pendingBattleSettlement is not null;
        _settlementBattle = battle; _settlementToken = pending.Token; _settlementAttempted = true;
        _pendingBattleSettlement = null; _battleSettlementError = null;
        try
        {
            var resources = BattleResources!;
            var result = BattleSettlement.Settle(battle.Definition, battle.Combatants,
                battle.Outcome == BattleOutcome.Victory, _battleExperienceFlag, resources.Progression!, resources.Items,
                Events!.DrawRandom, _inventory, _itemIds);
            _pendingBattleSettlement = result; LastBattleSettlement = result;
            PresentationRevision++;
            return true;
        }
        catch (Exception error)
        {
            // Settle calculates on copies before committing roles/inventory. Its shared
            // random callback cannot rewind; cache this failure instead of retrying every frame.
            _battleSettlementError = $"战后结算未完成：{error.Message}";
            PresentationRevision++;
            return false;
        }
    }

    public bool FinishBattle()
    {
        if (!PrepareBattleSettlement() || Battle is not { } battle || Events?.PendingBattle is not { } pending) return false;
        // DOS runs the last hurt/poison tick after result messages. Preparation deliberately
        // retains the frozen battlefield outcome until this final acknowledgement.
        battle.CompleteAfterSettlement();
        _battleHpCostScale = battle.HpCostScale;
        LastBattleOutcome = battle.Outcome;
        Battle = null;
        PresentationRevision++;
        return Events!.ResumeBattle(pending.Token);
    }

    bool IEventWorld.TryGetBattleOutcome(out bool victory)
    {
        victory = LastBattleOutcome == BattleOutcome.Victory;
        return Battle is null && LastBattleOutcome.HasValue;
    }
}
