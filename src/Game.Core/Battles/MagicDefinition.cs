using System.Text.Json.Serialization;

namespace JinYong.Core.Battles;

// OpenLegend model/game_snapshot.hpp magic_word: 136 bytes, not 99/107 words.
public static class MagicWords
{
    public const int Count = 68, Id = 0, SoundId = 11, MagicType = 12, EffectId = 13,
        HurtType = 14, AttackAreaType = 15, NeedMp = 16, WithPoison = 17,
        AttackBegin = 18, LevelCount = 10, SelectDistanceBegin = 28,
        AttackDistanceBegin = 38, AddMpBegin = 48, HurtMpBegin = 58;
}

/// <summary>Immutable RANGER magic record, preserving signed raw bits including name/unknown words.</summary>
public sealed class MagicDefinition
{
    [JsonConstructor]
    public MagicDefinition(int id, string name, IReadOnlyList<int> words)
    {
        if (id < 0 || words.Count != MagicWords.Count || words.Any(word => word is < short.MinValue or > short.MaxValue))
            throw new ArgumentException("Magic definition requires a nonnegative ID and 68 signed 16-bit words.");
        if (words[MagicWords.Id] != id) throw new ArgumentException("Magic ID differs from its original record.");
        Id = id; Name = name;
        Words = Array.AsReadOnly(words.ToArray());
    }
    public int Id { get; }
    public string Name { get; }
    public IReadOnlyList<int> Words { get; }
    public int this[int word] => Words[word];
}
