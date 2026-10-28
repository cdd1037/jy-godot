using JinYong.Core.Battles;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class BattleAttackRulesTests
{
    // Independently authored definitions. Numeric expectations are the original
    // machine vectors in OpenLegend Z_DAT/0x39188.md and 0x395EC.md, not NG+.
    [TestMethod]
    public void HpDamageMatchesOriginalBaselineAndStrictUnderkillReward()
    {
        var party = Party(); var magic = Magic(); var actor = party.Roles[0]; var target = party.Roles[1];
        var random = new RandomSource();
        var result = BattleAttackRules.HpDamage(actor,target,magic,299,1,random.Next);
        Assert.AreEqual(new BattleHpHit(10,3,3,30,3,6),result);
        Assert.AreEqual(40,target[RoleWords.Hp]); // Kernel returns data without mutating the session.
        CollectionAssert.AreEqual(new[] {20,20},random.Bounds);
        party.SetWord(1,RoleWords.Hp,30);
        Assert.AreEqual(6,BattleAttackRules.HpDamage(actor,target,magic,299,1,new RandomSource().Next).RewardExperience);
        party.SetWord(1,RoleWords.Hp,29);
        Assert.AreEqual(46,BattleAttackRules.HpDamage(actor,target,magic,299,1,new RandomSource().Next).RewardExperience);
    }

    [TestMethod]
    public void MpAffordabilityDescendsButPoisonUsesTheLearnedLevel()
    {
        var party = Party(); var magic = Magic((MagicWords.WithPoison,4));
        party.SetWord(0,RoleWords.Mp,49);
        Assert.AreEqual(9,BattleAttackRules.HpDamage(party.Roles[0],party.Roles[1],magic,999,1,new RandomSource().Next).CostScale);
        party.SetWord(0,RoleWords.Mp,0); party.SetWord(0,RoleWords.AttackWithPoison,5); party.SetWord(1,RoleWords.AntiPoison,0);
        var result = BattleAttackRules.HpDamage(party.Roles[0],party.Roles[1],magic,999,1,new RandomSource().Next);
        Assert.AreEqual(1,result.CostScale); Assert.AreEqual(3,result.Poison);
    }

    [TestMethod]
    public void FallbackDistanceKnowledgeAndConditionalRandomOrderMatchOriginal()
    {
        var party = Party(); var random = new RandomSource();
        party.SetWord(1,RoleWords.Hp,100); party.SetWord(1,RoleWords.AntiPoison,100);
        var result = BattleAttackRules.HpDamage(party.Roles[0],party.Roles[1],Magic(),299,11,random.Next,
            attackEquipment:6,defenceEquipment:2,specialBonus:3,alliedKnowledge:162,enemyKnowledge:164);
        Assert.AreEqual(14,result.Damage);
        CollectionAssert.AreEqual(new[] {20,20,4,4},random.Bounds);
        Assert.AreEqual(3295386429u,random.State);
        party.SetWord(0,RoleWords.Attack,0); party.SetWord(0,RoleWords.PhysicalPower,300);
        party.SetWord(1,RoleWords.Defence,20); party.SetWord(1,RoleWords.Hurt,80);
        var zeroAttack = Magic(); var words = zeroAttack.Words.ToArray();
        for (var level = 0; level < 10; level++) words[MagicWords.AttackBegin+level] = 0;
        result = BattleAttackRules.HpDamage(party.Roles[0],party.Roles[1],new(1,"fixture",words),299,1,new RandomSource().Next);
        Assert.AreEqual(1,result.Damage); // Negative fallback skips physical/hurt modifiers.
    }

    [TestMethod]
    public void HurtAndPoisonKeepOriginalNinetyAndOneHundredThresholds()
    {
        var party = Party(); var magic = Magic((MagicWords.WithPoison,4));
        party.SetWord(1,RoleWords.Hurt,98); party.SetWord(0,RoleWords.AttackWithPoison,1460);
        party.SetWord(1,RoleWords.AntiPoison,0);
        var hit = BattleAttackRules.HpDamage(party.Roles[0],party.Roles[1],magic,999,1,new RandomSource().Next);
        Assert.AreEqual(99,hit.Hurt); Assert.AreEqual(100,hit.Poison);
        party.SetWord(0,RoleWords.AttackWithPoison,1475);
        Assert.AreEqual(99,BattleAttackRules.HpDamage(party.Roles[0],party.Roles[1],magic,999,1,new RandomSource().Next).Poison);
        party.SetWord(1,RoleWords.AntiPoison,90);
        Assert.AreEqual(0,BattleAttackRules.HpDamage(party.Roles[0],party.Roles[1],magic,999,1,new RandomSource().Next).Poison);
    }

    [TestMethod]
    public void MpDamageUsesFiveOrderedDrawsAndOriginalMaximumCap()
    {
        var party = Party(); party.SetWord(0,RoleWords.Mp,10); party.SetWord(0,RoleWords.MaximumMp,20);
        party.SetWord(1,RoleWords.Mp,50);
        var random = new RandomSource();
        var result = BattleAttackRules.MpDamage(party.Roles[0],party.Roles[1],Magic(),299,random.Next);
        Assert.AreEqual(new BattleMpHit(23,23,35,15),result);
        CollectionAssert.AreEqual(new[] {3,3,10,3,3},random.Bounds);
        Assert.AreEqual(4182499122u,random.State);
        party.SetWord(0,RoleWords.MaximumMp,998);
        Assert.AreEqual(999,BattleAttackRules.MpDamage(party.Roles[0],party.Roles[1],Magic(),299,new RandomSource().Next).ActorMaximumMp);
    }

    [TestMethod]
    public void MpZeroAddSkipsItsRandomStateAndSameRoleUsesUpdatedMp()
    {
        var party = Party(); party.SetWord(0,RoleWords.Mp,10); party.SetWord(0,RoleWords.MaximumMp,20); party.SetWord(1,RoleWords.Mp,50);
        var words = Magic().Words.ToArray();
        for (var level = 0; level < 10; level++) words[MagicWords.AddMpBegin+level] = 0;
        var random = new RandomSource();
        var result = BattleAttackRules.MpDamage(party.Roles[0],party.Roles[1],new(1,"fixture",words),299,random.Next);
        CollectionAssert.AreEqual(new[] {3,3,3,3},random.Bounds);
        Assert.AreEqual(14,result.Damage);
        result = BattleAttackRules.MpDamage(party.Roles[0],party.Roles[0],Magic(),299,new RandomSource().Next);
        Assert.AreEqual(new BattleMpHit(8,23,8,2),result);
        party.SetWord(0,RoleWords.MaximumMp,32767);
        Assert.AreEqual(-32766,BattleAttackRules.MpDamage(party.Roles[0],party.Roles[1],Magic(),299,new RandomSource().Next).ActorMp);
    }

    [TestMethod]
    public void EqualRoleIdsDoNotAliasIndependentEnemyState()
    {
        var party = Party(); party.SetWord(0,RoleWords.Mp,10); party.SetWord(0,RoleWords.MaximumMp,20);
        var words = Words(0); words[RoleWords.Mp] = 50; words[RoleWords.MaximumMp] = 100;
        var enemy = new PartyState(new PartyDefinition([new(0,"enemy baseline","",words)],[0,-1,-1,-1,-1,-1])).Roles[0];
        var actor = party.Roles[0];
        Assert.AreEqual(actor.Id,enemy.Id); Assert.AreNotSame(actor,enemy);
        var result = BattleAttackRules.MpDamage(actor,enemy,Magic(),299,new RandomSource().Next);
        Assert.AreEqual(new BattleMpHit(23,23,35,15),result);
        Assert.AreEqual(10,actor[RoleWords.Mp]); Assert.AreEqual(50,enemy[RoleWords.Mp]);
    }

    [TestMethod]
    public void EachAttackIterationGrowsProficiencyBeforeMpAndWholeAttackPaysPowerOnce()
    {
        var party = Party(); party.SetWord(0,RoleWords.Mp,3);
        var random = new RandomSource();
        Assert.AreEqual(new BattleAttackIteration(300,true,0),BattleAttackRules.FinishIteration(party.Roles[0],0,Magic(),3,random.Next));
        CollectionAssert.AreEqual(new[] {2},random.Bounds);
        party.SetWord(0,RoleWords.MagicLevelBegin,999);
        Assert.AreEqual(999,BattleAttackRules.FinishIteration(party.Roles[0],0,Magic(),0,new RandomSource().Next).Proficiency);
        party.SetWord(0,RoleWords.MagicLevelBegin,65535);
        Assert.AreEqual(0,BattleAttackRules.FinishIteration(party.Roles[0],0,Magic(),0,new RandomSource().Next).Proficiency);
        Assert.AreEqual(0,BattleAttackRules.FinishAttackPhysicalPower(2));
        Assert.AreEqual(97,BattleAttackRules.FinishAttackPhysicalPower(100));
        Assert.AreEqual(32765,BattleAttackRules.FinishAttackPhysicalPower(short.MinValue));
    }

    [TestMethod]
    public void ProfilesUseProficiencyAndExactDoubleStrikeFlagWithoutInventingAttacks()
    {
        var party = Party(); var magic = Magic();
        var profile = BattleMagicRules.Profile(party.Roles[0],magic,0);
        Assert.AreEqual(2,profile.LevelIndex); Assert.AreEqual(1,profile.AttackCount);
        party.SetWord(0,RoleWords.AttackTwice,1);
        Assert.AreEqual(2,BattleMagicRules.Profile(party.Roles[0],magic,0).AttackCount);
        party.SetWord(0,RoleWords.AttackTwice,2);
        Assert.AreEqual(1,BattleMagicRules.Profile(party.Roles[0],magic,0).AttackCount);
        party.SetWord(0,RoleWords.Mp,9);
        Assert.IsEmpty(BattleMagicRules.AvailableMagicSlots(party.Roles[0],new Dictionary<int,MagicDefinition>{{1,magic}}));
        party.SetWord(0,RoleWords.MagicIdBegin,0);
        Assert.IsEmpty(BattleMagicRules.AvailableMagicSlots(party.Roles[0],new Dictionary<int,MagicDefinition>()));
        Assert.ThrowsExactly<ArgumentException>(() => BattleMagicRules.Profile(party.Roles[0],magic,0));
    }

    [TestMethod]
    public void AreasPreserveSquareCrossLineOrderAndForcedHpBranches()
    {
        var profile = new BattleAttackProfile(0,1,0,2,1,3,1,1,0);
        var square = BattleMagicRules.AreaCells(profile,new(10,10),new(12,12),Direction.Down);
        CollectionAssert.AreEqual(new GridPosition[] {new(11,11),new(11,12),new(11,13),new(12,11),new(12,12),new(12,13),new(13,11),new(13,12),new(13,13)},
            square.Select(cell => cell.Position).ToArray());
        Assert.IsTrue(square.All(cell => !cell.ForceHp));
        var cross = BattleMagicRules.AreaCells(profile with {AreaType=2},new(10,10),new(12,12),Direction.Down);
        CollectionAssert.AreEqual(new GridPosition[] {new(10,9),new(10,11),new(9,10),new(11,10),new(10,8),new(10,12),new(8,10),new(12,10)},
            cross.Select(cell => cell.Position).ToArray());
        Assert.IsTrue(cross.All(cell => cell.ForceHp));
        var line = BattleMagicRules.AreaCells(profile with {AreaType=1},new(10,10),new(12,12),Direction.Left);
        CollectionAssert.AreEqual(new GridPosition[] {new(9,10),new(8,10)},line.Select(cell => cell.Position).ToArray());
        Assert.IsTrue(line.All(cell => cell.ForceHp));
        Assert.AreEqual(Direction.Left,BattleMagicRules.Facing(new(10,10),new(10,10)));
        Assert.AreEqual(Direction.Right,BattleMagicRules.Facing(new(10,10),new(11,11)));
    }

    [TestMethod]
    public void EquipmentAndKnowledgeUseOriginalRawValuesWithoutPlaythroughMultipliers()
    {
        var party = Party();
        party.SetWord(0,RoleWords.EquipmentBegin,106); party.SetWord(0,RoleWords.EquipmentBegin+1,116);
        var first = new int[ItemWords.Count]; first[0]=106; first[ItemWords.AddAttack]=20; first[ItemWords.AddDefence]=5;
        var second = new int[ItemWords.Count]; second[0]=116; second[ItemWords.AddAttack]=7; second[ItemWords.AddDefence]=-2;
        var items = new Dictionary<int,ItemDefinition>{{106,new(106,"weapon",first)},{116,new(116,"armor",second)}};
        Assert.AreEqual((27,3),BattleAttackRules.EquipmentBonuses(party.Roles[0],items));
        Assert.AreEqual(100,BattleAttackRules.SpecialBonus(106,57));
        Assert.AreEqual(0,BattleAttackRules.SpecialBonus(106,1));
        party.SetWord(0,RoleWords.Hp,1); party.SetWord(0,RoleWords.Knowledge,81); party.SetWord(1,RoleWords.Knowledge,82);
        Assert.AreEqual(326,BattleAttackRules.KnowledgeBonus(party.Roles.Values));
        party.SetWord(1,RoleWords.Hp,0);
        Assert.AreEqual(162,BattleAttackRules.KnowledgeBonus(party.Roles.Values));
    }

    private static PartyState Party()
    {
        var actor = Words(0); var target = Words(1);
        actor[RoleWords.Attack]=30; actor[RoleWords.Mp]=actor[RoleWords.MaximumMp]=100;
        actor[RoleWords.AttackWithPoison]=60; actor[RoleWords.MagicIdBegin]=1; actor[RoleWords.MagicLevelBegin]=299;
        target[RoleWords.Hp]=target[RoleWords.MaximumHp]=40; target[RoleWords.Defence]=5;
        target[RoleWords.Level]=4; target[RoleWords.AntiPoison]=30;
        return new(new PartyDefinition([new(0,"actor","",actor),new(1,"target","",target)],[0,-1,-1,-1,-1,-1]));
    }
    private static int[] Words(int id)
    {
        var words = new int[RoleWords.Count]; words[0]=id;
        words[RoleWords.EquipmentBegin]=words[RoleWords.EquipmentBegin+1]=-1;
        for (var slot=0;slot<4;slot++) words[RoleWords.TakingItemBegin+slot]=-1;
        return words;
    }
    private static MagicDefinition Magic(params (int Word,int Value)[] changes)
    {
        var words = new int[MagicWords.Count]; words[0]=1; words[MagicWords.NeedMp]=10; words[MagicWords.WithPoison]=7;
        for (var level=0;level<10;level++)
        {
            words[MagicWords.AttackBegin+level]=30; words[MagicWords.SelectDistanceBegin+level]=1;
            words[MagicWords.AddMpBegin+level]=20; words[MagicWords.HurtMpBegin+level]=15;
        }
        foreach (var change in changes) words[change.Word]=change.Value;
        return new(1,"fixture",words);
    }
    private sealed class RandomSource
    {
        public uint State { get; private set; }=1;
        public List<int> Bounds { get; }=[];
        public int Next(int bound)
        {
            Bounds.Add(bound); State=unchecked(State*0x41C64E6D+0x3039);
            return (int)((State>>16)&0x7FFF)%bound;
        }
    }
}
