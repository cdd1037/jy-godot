namespace JinYong.Host;

/// <summary>Display values detached from the live battle. Collections are copied on construction.</summary>
public sealed record BattleResultsModel
{
    public string Title { get; }
    public string Summary { get; }
    public IReadOnlyList<BattleResultSection> Sections { get; }

    public BattleResultsModel(string title, string summary, IReadOnlyList<BattleResultSection> sections)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(sections);
        Title = title;
        Summary = summary;
        Sections = Array.AsReadOnly(sections.Select(section => new BattleResultSection(section.Title, section.Lines)).ToArray());
    }
}

public sealed record BattleResultSection
{
    public string Title { get; }
    public IReadOnlyList<string> Lines { get; }

    public BattleResultSection(string title, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(title);
        ArgumentNullException.ThrowIfNull(lines);
        Title = title;
        Lines = Array.AsReadOnly(lines.ToArray());
    }
}
