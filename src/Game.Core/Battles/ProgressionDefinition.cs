using System.Text.Json.Serialization;

namespace JinYong.Core.Battles;

/// <summary>Parsed DOS baseline rules only; no executable bytes or new-game-plus extension.</summary>
public sealed class ProgressionDefinition
{
    public const int OriginalLevelCount = 30;

    [JsonConstructor]
    public ProgressionDefinition(IReadOnlyList<int> experienceThresholds, int maximumHp, int maximumMp,
        int aptitudeBase, int aptitudeStep, int unlearnedLevel, int unassociatedFirstLevel,
        int rewardNumerator, int rewardDenominator)
    {
        if (experienceThresholds.Count != OriginalLevelCount || experienceThresholds[0] != 0 ||
            experienceThresholds.Any(value => value is < 0 or > ushort.MaxValue) ||
            experienceThresholds.Skip(1).Where((value, index) => value <= experienceThresholds[index]).Any())
            throw new ArgumentException("Progression requires thirty increasing unsigned 16-bit thresholds starting at zero.");
        if (maximumHp <= 0 || maximumMp <= 0 || aptitudeStep <= 0 || aptitudeBase <= 100 / aptitudeStep ||
            unlearnedLevel <= 0 || unassociatedFirstLevel <= 0 || rewardNumerator < 0 || rewardDenominator <= 0)
            throw new ArgumentException("Invalid original progression/practice rule parameters.");
        ExperienceThresholds = Array.AsReadOnly(experienceThresholds.ToArray());
        MaximumHp = maximumHp; MaximumMp = maximumMp;
        AptitudeBase = aptitudeBase; AptitudeStep = aptitudeStep; UnlearnedLevel = unlearnedLevel;
        UnassociatedFirstLevel = unassociatedFirstLevel; RewardNumerator = rewardNumerator; RewardDenominator = rewardDenominator;
    }

    public IReadOnlyList<int> ExperienceThresholds { get; }
    public int MaximumHp { get; }
    public int MaximumMp { get; }
    public int AptitudeBase { get; }
    public int AptitudeStep { get; }
    public int UnlearnedLevel { get; }
    public int UnassociatedFirstLevel { get; }
    public int RewardNumerator { get; }
    public int RewardDenominator { get; }
}
