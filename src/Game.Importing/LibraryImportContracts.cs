using JinYong.Importing.Legacy;

namespace JinYong.Importing;

public sealed record LibraryImportOptions(string SourceDirectory, string OutputDirectory,
    bool IncludeStory = true, LegacyScriptDialect ScriptDialect = LegacyScriptDialect.DosOriginal);
public sealed record LibraryImportReport(string LibraryDirectory, int Scenes, int SpriteBanks, int UniqueSprites,
    int Programs, int UnavailablePrograms, int SourceResources);
