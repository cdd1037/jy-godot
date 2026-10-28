using JinYong.Importing;
using JinYong.Importing.Legacy;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using System.Text.Json;

try
{
    if (args.Length == 0) return Usage();
    string Required(string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : throw new ArgumentException($"Missing {name}.");
    }
    string? Optional(string name) => Array.IndexOf(args, name) >= 0 ? Required(name) : null;
    switch (args[0])
    {
        case "import":
            if (args.Contains("--scene") || args.Contains("--spawn"))
                throw new ArgumentException("Import creates the complete shared library. Select --scene when loading or running it; --spawn is not an import option.");
            var dialect = Optional("--script-profile") switch
            {
                null or "dos-original" => LegacyScriptDialect.DosOriginal,
                "extended-50" => LegacyScriptDialect.Extended50,
                _ => throw new ArgumentException("Unknown --script-profile; use dos-original or extended-50.")
            };
            var report = new LegacyGameImporter().Import(new LibraryImportOptions(Required("--source"), Required("--output"), !args.Contains("--maps-only"), dialect));
            Console.WriteLine($"Imported library: {report.Scenes} scenes; {report.SpriteBanks} sprite banks; {report.UniqueSprites} unique sprites.");
            Console.WriteLine($"Story: {report.Programs} programs, {report.UnavailablePrograms} unavailable; {report.SourceResources} source resources cached.");
            Console.WriteLine(report.LibraryDirectory);
            return 0;
        case "inspect":
            var directory = Path.GetFullPath(Optional("--library") ?? Required("--package"));
            using (var manifestJson = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json"))))
            {
                if (manifestJson.RootElement.GetProperty("format_version").GetInt32() == 3)
                {
                    var library = GameLibrary.Load(directory);
                    var manifest = library.Manifest;
                    var scenes = library.Scenes;
                    var sprites = library.Sprites;
                    var gameplay = library.GameData;
                    var sources = library.Sources;
                    var audio = library.Audio;
                    Console.WriteLine($"Library format 3 | scenes={scenes.Count} | metadata={scenes.Count(scene => scene.HasLegacyMetadata)} | default scene={manifest.DefaultScene}");
                    Console.WriteLine($"Sprites: {sprites.Banks.Count} banks, {sprites.Assets.Count} unique assets; source resources={sources.Count}.");
                    Console.WriteLine($"Gameplay: roles={gameplay.Party?.Roles.Count ?? 0}; programs={gameplay.Programs.Length}; dialogues={gameplay.Dialogues.Length}; unavailable={gameplay.UnavailablePrograms.Count}.");
                    Console.WriteLine($"Audio: effects={audio.Effects.Count}; attacks={audio.Attacks.Count}; music={audio.Music.Count}.");
                    Console.WriteLine($"Verified {manifest.FileHashes.Count} files; {manifest.SourceHashes.Count} original resource fingerprints.");
                    return 0;
                }
            }
            var package = ScenePackageReader.Load(directory);
            Console.WriteLine("Legacy scene package (read-only compatibility).");
            Console.WriteLine($"{package.Scene.Id} | {package.Scene.Name} | spawn={package.Scene.Spawn} | sprites={package.Document.Sprites.Length}");
            Console.WriteLine($"Verified {package.Manifest.FileHashes.Count} files; {package.Manifest.SourceHashes.Count} original resource fingerprints.");
            if (package.Story is { } story) Console.WriteLine($"Story: {story.Programs.Count} programs, {story.Dialogues.Count} dialogues; opening={story.InitialProgramId}; unavailable={story.UnavailablePrograms.Count}.");
            return 0;
        case "event-demo":
            throw new ArgumentException("The event demo is a story overlay over a shared library. Use run.ps1 -EventDemo; no copied resource package is generated.");
        case "audit-events":
            var audit = LegacyEventAudit.Read(Required("--source"));
            var auditPath = Path.GetFullPath(Required("--output"));
            var sourcePath = Path.GetFullPath(Required("--source")).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (auditPath.StartsWith(sourcePath, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Audit output must be outside the original resource directory.");
            if (File.Exists(auditPath)) throw new IOException("Audit output already exists.");
            Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
            ContentJson.Write(auditPath, audit);
            Console.WriteLine($"Compiler accepted {audit.CompilerAcceptedCount}/{audit.Scripts.Length} nonempty scripts; rejected {audit.RejectedCount}. Runtime verification is separate.");
            Console.WriteLine(auditPath);
            return 0;
        default: return Usage();
    }
}
catch (Exception error) when (error is IOException or ArgumentException or InvalidOperationException or NotSupportedException or FormatException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine($"ERROR: {error.Message}");
    return 1;
}

static int Usage()
{
    Console.WriteLine("Game.Tools import --source ORIGINAL_DIR --output NEW_LIBRARY_DIR [--maps-only] [--script-profile dos-original]");
    Console.WriteLine("Game.Tools inspect --library LIBRARY_DIR (or --package for a legacy package)");
    Console.WriteLine("Game.Tools audit-events --source ORIGINAL_DIR --output NEW_REPORT_JSON");
    return 2;
}
