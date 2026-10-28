using JinYong.Core.Battles;
using JinYong.Core.Events;
using JinYong.Core.Items;
using JinYong.Core.Maps;
using JinYong.Core.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JinYong.Tests;

[TestClass]
public sealed class ExplorationItemTests
{
    [TestMethod]
    public void OriginalMpTypesOnlyRoleAndBothIqThresholdDirectionsRemainDistinct()
    {
        var session = Session([Item(1,1)]); var role = session.Party!.Roles[0];
        session.Party.SetWord(0,RoleWords.MpType,0);
        Assert.IsFalse(ItemEligibilityRules.MeetsRequirements(role,Item(1,1,(ItemWords.NeedMpType,1))));
        Assert.IsTrue(ItemEligibilityRules.MeetsRequirements(role,Item(1,1,(ItemWords.NeedMpType,2))));
        session.Party.SetWord(0,RoleWords.MpType,2);
        Assert.IsTrue(ItemEligibilityRules.MeetsRequirements(role,Item(1,1,(ItemWords.NeedMpType,1))));
        Assert.IsFalse(ItemEligibilityRules.MeetsRequirements(role,Item(1,1,(ItemWords.OnlySuitableRole,1))));
        foreach (var (need,expected) in new[] {(60,true),(61,false),(-60,true),(-59,false),(short.MinValue,true)})
            Assert.AreEqual(expected,ItemEligibilityRules.MeetsRequirements(role,Item(1,1,(ItemWords.NeedIq,need))),$"IQ={need}");
    }

    [TestMethod]
    public void EveryOriginalSkillGateReadsCurrentRoleValueAndIncludesEquality()
    {
        var session = Session([Item(1,1)]); var role = session.Party!.Roles[0];
        (int Role,int Need)[] fields =
        [(RoleWords.Mp,ItemWords.NeedMp),(RoleWords.Attack,ItemWords.NeedAttack),(RoleWords.Speed,ItemWords.NeedSpeed),
         (RoleWords.UsePoison,ItemWords.NeedUsePoison),(RoleWords.Medicine,ItemWords.NeedMedicine),
         (RoleWords.Detoxification,ItemWords.NeedDetoxification),(RoleWords.Fist,ItemWords.NeedFist),
         (RoleWords.Sword,ItemWords.NeedSword),(RoleWords.Knife,ItemWords.NeedKnife),
         (RoleWords.Unusual,ItemWords.NeedUnusual),(RoleWords.HiddenWeapon,ItemWords.NeedHiddenWeapon)];
        foreach (var (word,need) in fields)
        {
            session.Party.SetWord(0,word,49);
            Assert.IsFalse(ItemEligibilityRules.MeetsRequirements(role,Item(1,1,(need,50))),$"word{word} below");
            session.Party.SetWord(0,word,50);
            Assert.IsTrue(ItemEligibilityRules.MeetsRequirements(role,Item(1,1,(need,50))),$"word{word} equal");
        }
    }

    [TestMethod]
    public void KnownPracticeMagicOverridesAllEarlierFailuresAndNativeLastSlotControlsFullness()
    {
        var book = Item(78,2,(ItemWords.MagicId,7),(ItemWords.NeedMpType,0),
            (ItemWords.OnlySuitableRole,1),(ItemWords.NeedAttack,500),(ItemWords.NeedIq,99));
        var session = Session([book]); var party = session.Party!; var role = party.Roles[0];
        party.SetWord(0,RoleWords.Sex,1); party.SetWord(0,RoleWords.MpType,1);
        Assert.IsFalse(ItemEligibilityRules.MeetsRequirements(role,book));
        party.SetWord(0,RoleWords.MagicIdBegin+3,7);
        Assert.IsTrue(ItemEligibilityRules.MeetsRequirements(role,book));
        party.SetWord(0,RoleWords.MagicIdBegin+9,9);
        Assert.IsTrue(ItemEligibilityRules.Check(role,book,ItemUseMode.Practice).Eligible);
        party.SetWord(0,RoleWords.MagicIdBegin+3,0);
        var plain = Item(1,2,(ItemWords.MagicId,7));
        Assert.IsFalse(ItemEligibilityRules.Check(role,plain,ItemUseMode.Practice).Eligible);
        // The DOS caller checks only the final slot, rather than counting all slots.
        party.SetWord(0,RoleWords.MagicIdBegin+9,0);
        Assert.IsTrue(ItemEligibilityRules.Check(role,plain,ItemUseMode.Practice).Eligible);
    }

    [TestMethod]
    public void EquippingTransfersBothDirectionsWithoutConsumingOrChangingBaseAttributes()
    {
        var session = Session([Item(1,1,(ItemWords.AddAttack,90)),Item(2,1)],new Dictionary<int,int>{{1,0},{2,1}});
        var party = session.Party!; party.SetWord(0,RoleWords.EquipmentBegin,1); party.SetWord(1,RoleWords.EquipmentBegin,2);
        var attack = party.GetWord(1,RoleWords.Attack); var original = party.Roles[1];
        Assert.IsTrue(session.EquipItem(1,1).Success);
        Assert.AreSame(original,party.Roles[1]); Assert.AreEqual(-1,party.GetWord(0,RoleWords.EquipmentBegin));
        Assert.AreEqual(1,party.GetWord(1,RoleWords.EquipmentBegin)); Assert.AreEqual(1,party.ItemUsers[1]); Assert.AreEqual(-1,party.ItemUsers[2]);
        Assert.AreEqual(attack,party.GetWord(1,RoleWords.Attack)); Assert.AreEqual(1,session.Inventory[1]); Assert.AreEqual(1,session.Inventory[2]);
        var armor = Session([Item(3,1,(ItemWords.EquipmentType,-7))]);
        Assert.IsTrue(armor.EquipItem(3,0).Success); Assert.AreEqual(3,armor.Party!.GetWord(0,RoleWords.EquipmentBegin+1));
        Assert.AreEqual(-1,armor.Party.GetWord(0,RoleWords.EquipmentBegin));
    }

    [TestMethod]
    public void PracticeRequiresTransferBeforeMutationAndClearsOnlyNativeExperienceFields()
    {
        var session = Session([Item(1,2,(ItemWords.MagicId,7)),Item(2,2,(ItemWords.MagicId,8))],
            new Dictionary<int,int>{{1,0},{2,1}});
        var party = session.Party!;
        party.SetWord(0,RoleWords.PracticeItem,1); party.SetWord(0,RoleWords.ItemExperience,123); party.SetWord(0,RoleWords.MakeItemExperience,456);
        party.SetWord(1,RoleWords.PracticeItem,2); party.SetWord(1,RoleWords.ItemExperience,234); party.SetWord(1,RoleWords.MakeItemExperience,567);
        Assert.AreEqual(ExplorationItemUseStatus.ConfirmTransfer,session.PracticeItem(1,1).Status);
        Assert.AreEqual(123,party.GetWord(0,RoleWords.ItemExperience)); Assert.AreEqual(2,party.GetWord(1,RoleWords.PracticeItem));
        Assert.IsTrue(session.PracticeItem(1,1,confirmTransfer:true).Success);
        Assert.AreEqual(-1,party.GetWord(0,RoleWords.PracticeItem)); Assert.AreEqual(0,party.GetWord(0,RoleWords.ItemExperience));
        Assert.AreEqual(456,party.GetWord(0,RoleWords.MakeItemExperience));
        Assert.AreEqual(1,party.GetWord(1,RoleWords.PracticeItem)); Assert.AreEqual(0,party.GetWord(1,RoleWords.ItemExperience));
        Assert.AreEqual(0,party.GetWord(1,RoleWords.MakeItemExperience));
        Assert.AreEqual(1,party.ItemUsers[1]); Assert.AreEqual(-1,party.ItemUsers[2]); Assert.AreEqual(1,session.Inventory[1]);
        party.SetWord(1,RoleWords.ItemExperience,99); party.SetWord(1,RoleWords.MakeItemExperience,88);
        Assert.AreEqual(ExplorationItemUseStatus.Unchanged,session.PracticeItem(1,1,confirmTransfer:true).Status);
        Assert.AreEqual(99,party.GetWord(1,RoleWords.ItemExperience)); Assert.AreEqual(88,party.GetWord(1,RoleWords.MakeItemExperience));
    }

    [TestMethod]
    public void SpecialManualsNeedExplicitCastrationAndFailedEligibilityNeverChangesSex()
    {
        var session = Session([Item(78,2,(ItemWords.MagicId,7))]); var party = session.Party!;
        party.SetWord(0,RoleWords.Sex,0);
        Assert.AreEqual(ExplorationItemUseStatus.ConfirmCastration,session.PracticeItem(78,0).Status);
        Assert.AreEqual(0,party.GetWord(0,RoleWords.Sex)); Assert.AreEqual(-1,party.GetWord(0,RoleWords.PracticeItem));
        Assert.IsTrue(session.PracticeItem(78,0,confirmCastration:true).Success);
        Assert.AreEqual(2,party.GetWord(0,RoleWords.Sex)); Assert.AreEqual(78,party.GetWord(0,RoleWords.PracticeItem));
        var denied = Session([Item(93,2,(ItemWords.MagicId,7),(ItemWords.NeedAttack,500))]);
        denied.Party!.SetWord(0,RoleWords.Sex,0);
        Assert.AreEqual(ExplorationItemUseStatus.Rejected,denied.PracticeItem(93,0,confirmCastration:true).Status);
        Assert.AreEqual(0,denied.Party.GetWord(0,RoleWords.Sex));
    }

    [TestMethod]
    public void MedicineUsesSharedEffectsAndRngAndConsumesOnlyAnEffectivePositiveInventorySlot()
    {
        var session = Session([Item(1,3,(ItemWords.AddHp,20)),Item(2,3)]); var party = session.Party!;
        party.SetWord(0,RoleWords.Hp,30); var before = session.Events!.CaptureIdleState().RandomState;
        Assert.IsTrue(session.GetItemUseTargets(1,ItemUseMode.Consume).All(target=>target.Eligible));
        Assert.AreEqual(before,session.Events.CaptureIdleState().RandomState);
        Assert.IsTrue(session.ConsumeItem(1,0).Success);
        Assert.AreEqual(58,party.GetWord(0,RoleWords.Hp)); Assert.IsFalse(session.Inventory.ContainsKey(1));
        Assert.IsFalse(((IEventWorld)session).ItemExists(1)); Assert.AreNotEqual(before,session.Events.CaptureIdleState().RandomState);
        Assert.AreEqual(ExplorationItemUseStatus.Rejected,session.ConsumeItem(1,0).Status);
        Assert.AreEqual(ExplorationItemUseStatus.Unchanged,session.ConsumeItem(2,0).Status); Assert.AreEqual(1,session.Inventory[2]);
    }

    [TestMethod]
    public void OffPartyRolesHiddenItemsAndLockedLifecycleCannotBeUsed()
    {
        var session = Session([Item(1,1),Item(2,3,(ItemWords.ShowIntroduction,0))]);
        CollectionAssert.AreEqual(new[]{0,1},session.GetItemUseTargets(1,ItemUseMode.Equip).Select(target=>target.RoleId).ToArray());
        Assert.AreEqual(ExplorationItemUseStatus.Rejected,session.EquipItem(1,2).Status);
        Assert.AreEqual(ExplorationItemUseStatus.Rejected,session.ConsumeItem(2,0).Status);
        session.RetireForReplacement();
        Assert.IsTrue(session.GetItemUseTargets(1,ItemUseMode.Equip).All(target=>!target.Eligible));
        Assert.AreEqual(ExplorationItemUseStatus.Rejected,session.EquipItem(1,0).Status);
    }

    private static ItemDefinition Item(int id,int type,params (int Word,int Value)[] changes)
    {
        var words = new int[ItemWords.Count]; words[0]=id; words[ItemWords.ItemType]=type;
        words[ItemWords.ShowIntroduction]=1; words[ItemWords.User]=words[ItemWords.MagicId]=words[ItemWords.OnlySuitableRole]=-1;
        words[ItemWords.NeedMpType]=2;
        foreach(var (word,value) in changes) words[word]=value;
        return new(id,$"Item {id}",words);
    }
    private static GameSession Session(ItemDefinition[] items,IReadOnlyDictionary<int,int>? users=null)
    {
        RoleDefinition Role(int id)
        {
            var words = new int[RoleWords.Count]; words[0]=id;
            words[RoleWords.Hp]=40; words[RoleWords.MaximumHp]=words[RoleWords.Mp]=words[RoleWords.MaximumMp]=100;
            words[RoleWords.PhysicalPower]=100; words[RoleWords.Sex]=2; words[RoleWords.Iq]=60;
            for(var word=RoleWords.Attack;word<=RoleWords.HiddenWeapon;word++) words[word]=50;
            words[RoleWords.EquipmentBegin]=words[RoleWords.EquipmentBegin+1]=words[RoleWords.PracticeItem]=-1;
            for(var slot=0;slot<RoleWords.TakingItemCount;slot++) words[RoleWords.TakingItemBegin+slot]=-1;
            return new(id,$"Role {id}","",words);
        }
        var ownership=items.ToDictionary(item=>item.Id,item=>users?.GetValueOrDefault(item.Id,-1) ?? -1);
        var party=new PartyDefinition([Role(0),Role(1),Role(2)],[0,1,-1,-1,-1,-1],ownership);
        var story=new StoryCatalog(70,null,0,null,Direction.Right,[],[],items.ToDictionary(item=>item.Id,item=>item.Name),
            items.ToDictionary(item=>item.Id,_=>1),party:party);
        return new(MovementTests.Scene(),story,battles:new BattleCatalog([],[],[],items));
    }
}
