using JinYong.Core.Battles;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

// Independent definitions and historical native animation contracts. These are
// elapsed-time adaptations, not DOS framebuffer/runtime differential tests.
[TestClass]
public sealed class BattlePresentationTests
{
    [TestMethod]
    public void MetadataPreflightRejectsInvalidEffectsTypesAndPointersWithoutChangingState()
    {
        var battle = Battle(); var role = battle.CurrentActor!.Role; var hp = role[RoleWords.Hp]; var random = battle.RandomState;
        BattlePresentationPlans.ValidateMagicMetadata(role, 2, 2);
        Assert.ThrowsExactly<ArgumentException>(() => BattlePresentationPlans.ValidateMagicMetadata(role, 5, 2));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BattlePresentationPlans.ValidateMagicMetadata(role, 2, 53));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BattlePresentationPlans.ValidateEffectMetadata(-1));
        var words = role.Definition.Words.ToArray(); words[25] = short.MaxValue;
        var invalid = new PartyState(new PartyDefinition([new RoleDefinition(0, "Invalid pointer fixture", "", words)], [0, -1, -1, -1, -1, -1]));
        Assert.ThrowsExactly<ArgumentException>(() => BattlePresentationPlans.ValidateMagicMetadata(invalid.Roles[0], 2, 2));
        BattlePresentationPlans.ValidateMagicMetadata(Battle(effectStart: -20).CurrentActor!.Role, 2, 2);
        Assert.AreEqual(hp, role[RoleWords.Hp]); Assert.AreEqual(random, battle.RandomState); Assert.IsFalse(battle.IsPresenting);
    }

    [TestMethod]
    public void NativeEffectTableDelayUnitsAndBankOffsetsMatchDistinguishingVectors()
    {
        Assert.HasCount(53, BattlePresentationPlans.EffectFrameCounts);
        Assert.AreEqual(10, BattlePresentationPlans.EffectFrameCounts[0]);
        Assert.AreEqual(17, BattlePresentationPlans.EffectFrameCounts[2]);
        Assert.AreEqual(11, BattlePresentationPlans.EffectFrameCounts[30]);
        Assert.AreEqual(13000, BattlePresentationPlans.EffectStartFrame(0));
        Assert.AreEqual(13048, BattlePresentationPlans.EffectStartFrame(2));
        Assert.AreEqual(13772, BattlePresentationPlans.EffectStartFrame(30));
        Assert.AreEqual(BattlePresentationPlans.BiosTick, BattlePresentationPlans.Delay(17));
        Assert.AreEqual(BattlePresentationPlans.BiosTick * 2, BattlePresentationPlans.Delay(40));
        Assert.AreEqual(BattlePresentationPlans.BiosTick * 3, BattlePresentationPlans.Delay(100));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BattlePresentationPlans.EffectStartFrame(-1));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BattlePresentationPlans.EffectStartFrame(53));
    }

    [TestMethod]
    public void OriginalFightType2VectorKeepsFinalActorSpriteAndStartsEachLoadedSoundOnce()
    {
        var battle = Battle(); var before = battle.CaptureCombatantSnapshots();
        var after = before.Select(person => person.Slot == 0 ? person with { Facing = Direction.Right } : person).ToArray();
        var plan = BattlePresentationPlans.Magic(before, after, 0, 2, 2, 7, [new(12, 10)],
            [new(1, new(12, 10), 21, BattleDamageKind.Hp)]);
        var fight = plan.Frames.Where(frame => frame.Stage == BattlePresentationStage.Fight).ToArray();
        Assert.HasCount(19, fight);
        CollectionAssert.AreEqual(new[] { 16048, 16050, 16052, 16054 }, fight.Take(4).Select(frame => frame.ActorSprite!.Frame).ToArray());
        Assert.IsTrue(fight.All(frame => frame.ActorSprite!.Bank == "fight/003"));
        Assert.IsTrue(fight.Skip(4).All(frame => frame.ActorSprite!.Frame == 16054));
        Assert.HasCount(0, fight[0].Effects); Assert.HasCount(0, fight[1].Effects);
        Assert.AreEqual(13048, fight[2].Effects.Single().Frame); Assert.AreEqual(13080, fight[^1].Effects.Single().Frame);
        Assert.AreEqual("effect", fight[2].Effects.Single().Bank);
        CollectionAssert.AreEqual(new[] { new BattlePresentationSound(BattleSoundBank.Attack, 7, BattleSoundAction.Load),
            new BattlePresentationSound(BattleSoundBank.Effect, 2, BattleSoundAction.Load) }, plan.InitialSounds.ToArray());
        Assert.AreEqual(new BattlePresentationSound(BattleSoundBank.Effect, 2, BattleSoundAction.StartLoaded), fight[2].Sounds.Single());
        Assert.AreEqual(new BattlePresentationSound(BattleSoundBank.Attack, 7, BattleSoundAction.StartLoaded), fight[4].Sounds.Single());
        Assert.HasCount(2, fight.SelectMany(frame => frame.Sounds).ToArray());
        var damage = plan.Frames.Where(frame => frame.Stage == BattlePresentationStage.Damage).ToArray();
        Assert.HasCount(10, damage); CollectionAssert.AreEqual(Enumerable.Range(0, 10).ToArray(), damage.Select(frame => frame.DamagePhase).ToArray());
        CollectionAssert.AreEqual(new[] { true, true, true, true, false, false, false, false, false, false }, damage.Select(frame => frame.Flash).ToArray());
        Assert.IsTrue(damage.All(frame => frame.ActorSprite!.Frame == 16054));
    }

    [TestMethod]
    public void SimultaneousSoundThresholdStartsAttackBeforeEffectAndSignedZeroFramesOnlyLoad()
    {
        var battle = Battle(effectStart: 1, soundStart: 1); var snapshots = battle.CaptureCombatantSnapshots();
        var plan = BattlePresentationPlans.Magic(snapshots, snapshots, 0, 2, 2, 7, [new(12, 10)], []);
        CollectionAssert.AreEqual(new[] { BattleSoundBank.Attack, BattleSoundBank.Effect }, plan.Frames[0].Sounds.Select(sound => sound.Bank).ToArray());
        var zero = Battle(effectStart: -20).CaptureCombatantSnapshots();
        plan = BattlePresentationPlans.Magic(zero, zero, 0, 2, 2, 7, [new(12, 10)], []);
        Assert.HasCount(0, plan.Frames.Where(frame => frame.Stage == BattlePresentationStage.Fight).ToArray());
        Assert.HasCount(2, plan.InitialSounds); Assert.HasCount(0, plan.Frames.SelectMany(frame => frame.Sounds).ToArray());
    }

    [TestMethod]
    public void ThrowingEffectHasNativeThreeTickPreludeElevenEffectFramesAndTenDamageFrames()
    {
        var snapshots = Battle().CaptureCombatantSnapshots();
        var plan = BattlePresentationPlans.ThrownItem(snapshots, snapshots, 0, 30, [new(12, 10)],
            [new(1, new(12, 10), 21, BattleDamageKind.Hp)]);
        Assert.AreEqual(BattlePresentationStage.EffectPrelude, plan.Frames[0].Stage);
        Assert.AreEqual(BattlePresentationPlans.Delay(100), plan.Frames[0].Duration); Assert.HasCount(0, plan.Frames[0].Effects);
        Assert.AreEqual(new BattlePresentationSound(BattleSoundBank.Attack, 13, BattleSoundAction.StartLoaded), plan.Frames[0].Sounds.Single());
        var effects = plan.Frames.Where(frame => frame.Stage == BattlePresentationStage.Effect).ToArray(); Assert.HasCount(11, effects);
        CollectionAssert.AreEqual(Enumerable.Range(0, 11).Select(index => 13772 + index * 2).ToArray(), effects.Select(frame => frame.Effects.Single().Frame).ToArray());
        Assert.AreEqual(new BattlePresentationSound(BattleSoundBank.Effect, 30, BattleSoundAction.StartLoaded), effects[0].Sounds.Single());
        Assert.HasCount(1, effects.SelectMany(frame => frame.Sounds).ToArray());
        Assert.HasCount(10, plan.Frames.Where(frame => frame.Stage == BattlePresentationStage.Damage).ToArray());
    }

    [TestMethod]
    public void MovementInterpolatesDetachedSnapshotsAndKeepsActorAfterItsLastWait()
    {
        var battle = Battle(present: true); var actor = battle.CurrentActor!; var original = battle.CaptureCombatantSnapshots();
        Assert.IsTrue(battle.TryMove(new(11, 11))); Assert.IsTrue(battle.IsPresenting);
        Assert.AreEqual(new GridPosition(11, 11), actor.Position); // rules committed; view traverses its captured path
        var frame = battle.Presentation!; Assert.AreEqual(BattlePresentationStage.Movement, frame.Stage);
        Assert.AreEqual(new GridPosition(10, 10), frame.Movement!.From); Assert.AreEqual(0d, frame.Movement.Progress);
        Assert.AreEqual("battlefield", frame.ActorSprite!.Bank);
        Assert.AreEqual(original[0].Position, frame.Combatants[0].Position);
        Assert.IsFalse(battle.TryMove(new(11, 10))); Assert.IsFalse(battle.Rest()); Assert.IsFalse(battle.Wait());
        battle.AdvancePresentation(BattlePresentationPlans.BiosTick);
        Assert.AreEqual(.5, battle.Presentation!.Movement!.Progress, .00001);
        battle.AdvancePresentation(BattlePresentationPlans.BiosTick);
        Assert.AreEqual(1, battle.Presentation!.FrameIndex); Assert.AreEqual(0d, battle.Presentation.Movement!.Progress);
        battle.AdvancePresentation(BattlePresentationPlans.BiosTick * 2);
        Assert.IsFalse(battle.IsPresenting); Assert.IsNull(battle.Presentation); Assert.AreSame(actor, battle.CurrentActor); Assert.IsFalse(actor.ActionDone);
        Assert.AreEqual(new GridPosition(10, 10), original[0].Position); // never mutated by movement
    }

    [TestMethod]
    public void AttackLocksCommandsRandomAndActorUntilTheCompleteDamageSequence()
    {
        var battle = Battle(present: true); var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.AttackMagic(0, new(12, 10))); Assert.IsTrue(battle.IsPresenting);
        var random = battle.RandomState; Assert.AreSame(actor, battle.CurrentActor); Assert.IsFalse(actor.ActionDone);
        Assert.IsFalse(battle.AttackMagic(0, new(12, 10))); Assert.IsFalse(battle.UsePoison(new(12, 10)));
        Assert.IsFalse(battle.UseBattleItem(1)); Assert.IsFalse(battle.ThrowItem(1, new(12, 10)));
        Assert.IsFalse(battle.TryMove(new(11, 10))); Assert.IsFalse(battle.Wait()); Assert.IsFalse(battle.Rest());
        Assert.AreEqual(random, battle.RandomState);
        var sounds = battle.TakePresentationSounds(); Assert.HasCount(2, sounds); Assert.IsTrue(sounds.All(sound => sound.Action == BattleSoundAction.Load));
        Assert.HasCount(0, battle.TakePresentationSounds());
        battle.AdvancePresentation(TimeSpan.Zero); Assert.AreEqual(0, battle.Presentation!.FrameIndex);
        var frames = Drain(battle);
        Assert.HasCount(19, frames.Where(frame => frame.Stage == BattlePresentationStage.Fight).ToArray());
        Assert.HasCount(10, frames.Where(frame => frame.Stage == BattlePresentationStage.Damage).ToArray());
        Assert.IsTrue(actor.ActionDone); Assert.IsFalse(battle.CurrentActor!.IsFriendly); Assert.AreEqual(random, battle.RandomState);
        Assert.HasCount(2, battle.TakePresentationSounds()); Assert.HasCount(0, battle.TakePresentationSounds());
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => battle.AdvancePresentation(TimeSpan.FromTicks(-1)));
    }

    [TestMethod]
    public void DefeatedTargetRemainsDrawableUntilDamageCompletesAndVictoryIsDeferred()
    {
        var battle = Battle(present: true, enemyHp: 1); var victim = battle.Combatants.Single(actor => !actor.IsFriendly);
        Assert.IsTrue(battle.AttackMagic(0, victim.Position)); Assert.AreEqual(0, victim.Hp);
        Assert.AreEqual(BattleOutcome.Ongoing, battle.EvaluateOutcome()); Assert.AreEqual(BattlePhase.Acting, battle.Phase);
        var frames = Drain(battle); var fight = frames.First(frame => frame.Stage == BattlePresentationStage.Fight);
        Assert.AreEqual(1, fight.Combatants.Single(person => person.Slot == victim.Slot).Hp);
        foreach (var frame in frames.Where(frame => frame.Stage == BattlePresentationStage.Damage))
        {
            var person = frame.Combatants.Single(person => person.Slot == victim.Slot);
            Assert.AreEqual(0, person.Hp); Assert.IsFalse(person.Hidden);
        }
        Assert.IsTrue(victim.Hidden); Assert.AreEqual(BattleOutcome.Victory, battle.Outcome); Assert.AreEqual(BattlePhase.Finished, battle.Phase);
    }

    [TestMethod]
    public void TwoAttackIterationsAppendTheirOwnDetachedHpSnapshotsBeforeOneActionCompletion()
    {
        var battle = Battle(present: true, twice: true); var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.AttackMagic(0, new(12, 10))); var frames = Drain(battle);
        var starts = frames.Where(frame => frame.Stage == BattlePresentationStage.Fight && frame.FrameIndex == 0).ToArray();
        Assert.HasCount(2, starts); Assert.HasCount(20, frames.Where(frame => frame.Stage == BattlePresentationStage.Damage).ToArray());
        var firstVictim = starts[0].After.Single(person => !person.IsFriendly);
        Assert.AreEqual(firstVictim.Hp, starts[1].Before.Single(person => !person.IsFriendly).Hp);
        Assert.IsTrue(starts[0].Before.Single(person => !person.IsFriendly).Hp > firstVictim.Hp);
        Assert.IsTrue(firstVictim.Hp > starts[1].After.Single(person => !person.IsFriendly).Hp);
        Assert.IsTrue(actor.ActionDone); Assert.IsFalse(battle.CurrentActor!.IsFriendly);
    }

    [TestMethod]
    public void DefaultDirectCoreActionsStayInstantAndClockNeverConsumesRuleRandomness()
    {
        var instant = Battle(); Assert.IsTrue(instant.AttackMagic(0, new(12, 10))); Assert.IsFalse(instant.IsPresenting);
        Assert.HasCount(0, instant.TakePresentationSounds()); Assert.IsFalse(instant.CurrentActor!.IsFriendly);
        var timed = Battle(present: true); Assert.IsTrue(timed.AttackMagic(0, new(12, 10)));
        var finalHp = timed.Combatants.Single(actor => !actor.IsFriendly).Hp; var random = timed.RandomState;
        timed.AdvancePresentation(TimeSpan.FromDays(1)); Assert.IsFalse(timed.IsPresenting);
        Assert.AreEqual(random, timed.RandomState); Assert.AreEqual(finalHp, timed.Combatants.Single(actor => !actor.IsFriendly).Hp);
        Assert.AreEqual(instant.Combatants.Single(actor => !actor.IsFriendly).Hp, finalHp);
    }

    [TestMethod]
    public void MedicineSupportSuppressesFlashAndKeepsHistoricalAttackAudioInsteadOfNgPlusSilencing()
    {
        var battle = Battle(present: true); Assert.IsTrue(battle.Medicine(new(10, 10)));
        var loads = battle.TakePresentationSounds(); Assert.IsTrue(loads.Any(sound => sound.Bank == BattleSoundBank.Attack && sound.Id == 7));
        var frames = Drain(battle); var damage = frames.Where(frame => frame.Stage == BattlePresentationStage.Damage).ToArray();
        Assert.HasCount(10, damage); Assert.IsTrue(damage.All(frame => !frame.Flash));
        Assert.AreEqual(BattleDamageKind.Medicine, damage[0].Damage.Single().Kind);
        Assert.IsTrue(battle.TakePresentationSounds().Any(sound => sound.Bank == BattleSoundBank.Attack && sound.Action == BattleSoundAction.StartLoaded));
    }

    [TestMethod]
    public void PlayerMedicinePanelWaitsForMatchingConfirmationAndCannotCompleteFromElapsedTime()
    {
        var battle = Battle(present: true, medicines: true); var actor = battle.CurrentActor!;
        Assert.IsTrue(battle.UseBattleItem(1)); var frame = battle.Presentation!;
        Assert.AreEqual(BattlePresentationStage.Item, frame.Stage); Assert.IsTrue(frame.WaitingForConfirmation);
        Assert.AreEqual(TimeSpan.Zero, frame.Duration); Assert.IsTrue(frame.Token > 0); Assert.IsNotNull(frame.ItemUse);
        Assert.AreEqual(70, actor.Mp); Assert.AreEqual(50, frame.Before.Single(person => person.Slot == actor.Slot).Mp);
        battle.AdvancePresentation(TimeSpan.FromDays(100)); Assert.IsTrue(battle.IsPresenting); Assert.AreEqual(frame.Token, battle.Presentation!.Token);
        Assert.IsFalse(battle.ConfirmPresentation(frame.Token + 1)); Assert.AreSame(actor, battle.CurrentActor); Assert.IsFalse(actor.ActionDone);
        Assert.IsTrue(battle.ConfirmPresentation(frame.Token)); Assert.IsFalse(battle.IsPresenting); Assert.IsTrue(actor.ActionDone);
        Assert.IsFalse(battle.ConfirmPresentation(frame.Token)); Assert.IsFalse(battle.CurrentActor!.IsFriendly);
    }

    [TestMethod]
    public void EnemyMedicinePanelUsesNineTickWaitAndNeverRequestsPlayerConfirmation()
    {
        var battle = Battle(present: true, medicines: true); Assert.IsTrue(battle.Rest());
        var actor = battle.CurrentActor!; Assert.IsFalse(actor.IsFriendly); Assert.IsTrue(battle.UseBattleItem(1));
        var frame = battle.Presentation!; Assert.IsFalse(frame.WaitingForConfirmation); Assert.AreEqual(BattlePresentationPlans.Delay(340), frame.Duration);
        Assert.IsFalse(battle.ConfirmPresentation(frame.Token));
        battle.AdvancePresentation(BattlePresentationPlans.BiosTick * 8); Assert.IsTrue(battle.IsPresenting);
        battle.AdvancePresentation(BattlePresentationPlans.BiosTick); Assert.IsFalse(battle.IsPresenting);
        Assert.AreEqual(2, battle.Round); Assert.IsTrue(battle.CurrentActor!.IsFriendly);
    }

    private static List<BattlePresentationFrame> Drain(BattleSession battle)
    {
        var result = new List<BattlePresentationFrame>();
        for (var budget = 0; battle.IsPresenting && budget < 1000; budget++)
        { var frame = battle.Presentation!; result.Add(frame); battle.AdvancePresentation(frame.Duration); }
        Assert.IsFalse(battle.IsPresenting); return result;
    }
    private static BattleSession Battle(bool present = false, bool twice = false, int enemyHp = 900, int effectStart = 3, int soundStart = 5, bool medicines = false)
    {
        RoleDefinition Role(int id)
        {
            var words = new int[91]; words[0] = id; words[RoleWords.HeadId] = id == 0 ? 3 : 0;
            words[RoleWords.Hp] = words[RoleWords.MaximumHp] = id == 0 ? 200 : enemyHp;
            words[RoleWords.Mp] = medicines ? 50 : 100; words[RoleWords.MaximumMp] = 100; words[RoleWords.PhysicalPower] = 100;
            words[RoleWords.Speed] = id == 0 ? 60 : 30; words[RoleWords.Attack] = 50; words[RoleWords.Defence] = 10;
            words[RoleWords.Medicine] = 60; words[RoleWords.Level] = 1; words[RoleWords.MagicIdBegin] = 1;
            words[RoleWords.EquipmentBegin] = words[RoleWords.EquipmentBegin + 1] = words[RoleWords.PracticeItem] = -1;
            for (var slot = 0; slot < 4; slot++) words[RoleWords.TakingItemBegin + slot] = -1;
            if (medicines) { words[RoleWords.TakingItemBegin] = 1; words[RoleWords.TakingCountBegin] = 1; }
            for (var type = 0; type < 5; type++) { words[25 + type] = 2; words[30 + type] = effectStart; words[35 + type] = soundStart; }
            words[26] = 3; words[27] = 4; words[RoleWords.AttackTwice] = twice && id == 0 ? 1 : 0;
            return new(id, $"Role {id}", "", words);
        }
        var party = new PartyState(new PartyDefinition([Role(0), Role(1)], [0, -1, -1, -1, -1, -1]));
        var definition = new int[93]; for (var index = 9; index < 21; index++) definition[index] = -1;
        for (var index = 33; index < 53; index++) definition[index] = -1;
        definition[15] = 0; definition[21] = 10; definition[27] = 10; definition[33] = 1; definition[53] = 12; definition[73] = 10;
        var magic = new int[68]; magic[0] = 1; magic[MagicWords.SoundId] = 7; magic[MagicWords.MagicType] = 2; magic[MagicWords.EffectId] = 2;
        for (var level = 0; level < 10; level++) { magic[MagicWords.AttackBegin + level] = 100; magic[MagicWords.SelectDistanceBegin + level] = 5; }
        var items = new Dictionary<int, ItemDefinition>();
        if (medicines) { var item = new int[95]; item[0] = 1; item[ItemWords.ItemType] = 3; item[ItemWords.AddMp] = 20; items[1] = new(1, "Fixture medicine", item); }
        return new(new(0, definition), new(0, new short[8192]), party, new Dictionary<int, MagicDefinition> { [1] = new(1, "Fixture magic", magic) },
            items, inventory: medicines ? new MedicineInventory() : null, presentActions: present);
    }
    private sealed class MedicineInventory : IBattleInventory
    {
        private readonly Dictionary<int, int> _items = new() { [1] = 1 };
        public IReadOnlyDictionary<int, int> Items => _items;
        public bool TryConsume(int itemId, int count)
        {
            if (count <= 0 || _items.GetValueOrDefault(itemId) < count) return false;
            _items[itemId] -= count; return true;
        }
    }
}
