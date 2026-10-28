namespace JinYong.Host;

public sealed record GameMenuEntry(string Id, string Text, bool Enabled = true, string? Hint = null);

/// <summary>Page content only. Controllers own navigation, authorization and game actions.</summary>
public sealed record GameMenuModel(string Title, string Subtitle, IReadOnlyList<GameMenuEntry> Entries,
    string Body = "", string? Error = null);
