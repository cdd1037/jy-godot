using System.Text.Json.Serialization;
using JinYong.Core.Maps;

namespace JinYong.Core.Battles;

// OpenLegend battle/battle_data.hpp and battle/battle_setup.cpp: WAR.STA layout.
public static class BattleWords
{
    public const int Count = 93, BattlefieldId = 6, Experience = 7, MusicId = 8,
        PresetPartyBegin = 9, FixedPartyBegin = 15, PartyXBegin = 21, PartyYBegin = 27,
        PartySlots = 6, EnemyBegin = 33, EnemyXBegin = 53, EnemyYBegin = 73, EnemySlots = 20;
}

/// <summary>Immutable 186-byte WAR.STA record. ID is its archive index; all raw words are retained.</summary>
public sealed class BattleDefinition
{
    [JsonConstructor]
    public BattleDefinition(int id, IReadOnlyList<int> words, string name = "")
    {
        if (id < 0 || words.Count != BattleWords.Count || words.Any(word => word is < short.MinValue or > short.MaxValue))
            throw new ArgumentException("Battle definition requires a nonnegative ID and 93 signed 16-bit words.");
        Id = id; Name = name;
        Words = Array.AsReadOnly(words.ToArray());
    }

    public int Id { get; }
    public string Name { get; }
    public IReadOnlyList<int> Words { get; }
    public int this[int word] => Words[word];
    [JsonIgnore] public int BattlefieldId => Words[BattleWords.BattlefieldId];
    [JsonIgnore] public int Experience => Words[BattleWords.Experience];
    [JsonIgnore] public int MusicId => Words[BattleWords.MusicId];
    public int PresetParty(int slot) => Words[BattleWords.PresetPartyBegin + PartySlot(slot)];
    public int FixedParty(int slot) => Words[BattleWords.FixedPartyBegin + PartySlot(slot)];
    public GridPosition PartyPosition(int slot) => new(Words[BattleWords.PartyXBegin + PartySlot(slot)], Words[BattleWords.PartyYBegin + slot]);
    public int EnemyRole(int slot) => Words[BattleWords.EnemyBegin + EnemySlot(slot)];
    public GridPosition EnemyPosition(int slot) => new(Words[BattleWords.EnemyXBegin + EnemySlot(slot)], Words[BattleWords.EnemyYBegin + slot]);
    private static int PartySlot(int slot) => slot is >= 0 and < BattleWords.PartySlots ? slot : throw new ArgumentOutOfRangeException(nameof(slot));
    private static int EnemySlot(int slot) => slot is >= 0 and < BattleWords.EnemySlots ? slot : throw new ArgumentOutOfRangeException(nameof(slot));
}
