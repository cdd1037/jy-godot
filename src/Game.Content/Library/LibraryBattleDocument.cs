using JinYong.Core.Battles;

namespace JinYong.Content.Library;

/// <summary>Global combat definitions; all fields share one binary file and the existing sprite catalog.</summary>
public sealed record LibraryBattleDocument
{
    public int FormatVersion { get; init; } = 1;
    public BattleDefinition[] Battles { get; init; } = [];
    public int[] BattlefieldIds { get; init; } = [];
    public MagicDefinition[] Magics { get; init; } = [];
    public ItemDefinition[] Items { get; init; } = [];
    public ProgressionDefinition? Progression { get; init; }
}
