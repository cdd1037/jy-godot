using System.Text.Json.Serialization;

namespace JinYong.Core.Battles;

// OpenLegend model/game_snapshot.hpp item_word: offsets are signed 16-bit word indices.
public static class ItemWords
{
    public const int Count = 95, Id = 0, MagicId = 36, HiddenWeaponEffectId = 37,
        User = 38, EquipmentType = 39, ShowIntroduction = 40, ItemType = 41,
        AddHp = 45, AddMaximumHp = 46, AddPoison = 47, AddPhysicalPower = 48,
        ChangeMpType = 49, AddMp = 50, AddMaximumMp = 51, AddAttack = 52,
        AddSpeed = 53, AddDefence = 54, AddMedicine = 55, AddUsePoison = 56,
        AddDetoxification = 57, AddAntiPoison = 58, AddFist = 59, AddSword = 60,
        AddKnife = 61, AddUnusual = 62, AddHiddenWeapon = 63, AddKnowledge = 64,
        AddMorality = 65, AddAttackTwice = 66, AddAttackWithPoison = 67,
        OnlySuitableRole = 68, NeedMpType = 69, NeedMp = 70, NeedAttack = 71,
        NeedSpeed = 72, NeedUsePoison = 73, NeedMedicine = 74, NeedDetoxification = 75,
        NeedFist = 76, NeedSword = 77, NeedKnife = 78, NeedUnusual = 79,
        NeedHiddenWeapon = 80, NeedIq = 81, NeedExperience = 82,
        NeedMakeItemExperience = 83, NeedMaterial = 84, MakeItemBegin = 85,
        MakeItemCount = 5, MakeItemCountBegin = 90;
}

/// <summary>Immutable 190-byte RANGER item record; description/name bytes and unused words are preserved.</summary>
public sealed class ItemDefinition
{
    [JsonConstructor]
    public ItemDefinition(int id, string name, IReadOnlyList<int> words)
    {
        if (id < 0 || words.Count != ItemWords.Count || words.Any(word => word is < short.MinValue or > short.MaxValue))
            throw new ArgumentException("Item definition requires a nonnegative ID and 95 signed 16-bit words.");
        if (words[ItemWords.Id] != id) throw new ArgumentException("Item ID differs from its original record.");
        Id = id; Name = name;
        Words = Array.AsReadOnly(words.ToArray());
    }
    public int Id { get; }
    public string Name { get; }
    public IReadOnlyList<int> Words { get; }
    public int this[int word] => Words[word];
}
