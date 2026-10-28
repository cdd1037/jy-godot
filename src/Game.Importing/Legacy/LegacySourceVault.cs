using System.Security.Cryptography;
using System.Text.RegularExpressions;
using JinYong.Content.Library;

namespace JinYong.Importing.Legacy;

/// <summary>Retains game data for future decoders, without copying DOS programs, drivers or saves.</summary>
internal static partial class LegacySourceVault
{
    public static Dictionary<string, SourceResource> Import(IEnumerable<string> filenames, Func<string, byte[]> read, string output)
    {
        var resources = new Dictionary<string, SourceResource>(StringComparer.OrdinalIgnoreCase);
        Directory.CreateDirectory(Path.Combine(output, "assets/source"));
        foreach (var name in filenames.Order(StringComparer.OrdinalIgnoreCase))
        {
            var kind = Kind(name);
            if (kind is null) continue;
            var bytes = read(name);
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var extension = kind is "audio" ? ".wav" : kind is "music-xmi" ? ".xmi" : ".bin";
            var relative = $"assets/source/{hash}{extension}";
            var path = Path.Combine(output, relative);
            if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
            resources.Add(name.ToUpperInvariant(), new(hash, bytes.LongLength, relative, kind));
        }
        return resources;
    }

    public static string? Kind(string filename)
    {
        var name = filename.ToUpperInvariant();
        if (SceneSprite().IsMatch(name)) return "scene-sprites";
        if (BattleSprite().IsMatch(name) || FightArchive().IsMatch(name)) return "battle-sprites";
        if (EffectAudio().IsMatch(name) || AttackAudio().IsMatch(name) || name == "TIT.WAV") return "audio";
        if (Music().IsMatch(name)) return "music-xmi";
        if (Archive().IsMatch(name)) return "archive";
        if (name is "MMAP.COL" or "ENDCOL.COL") return "palette";
        if (name is "BUILDING.002" or "BUILDX.002" or "BUILDY.002" or "EARTH.002" or "SURFACE.002") return "world-map";
        if (name is "HERO.STA" or "WAR.STA") return "battle-data";
        if (name is "FONT.C16" or "FONT.E16" or "FONT.X16" or "FONT3.C16" or "FONT3.E16" or "CFONT") return "font";
        if (name is "DEAD.BIG" or "TITLE.BIG" or "TIT2.MOV" or "3_SHADOW.MSK" or "4_SHADOW.MSK" or "SOL") return "presentation";
        return null;
    }

    [GeneratedRegex("^(SDX|SMP)[0-9]{3}$")] private static partial Regex SceneSprite();
    [GeneratedRegex("^(WDX|WMP)[0-9]{3}$")] private static partial Regex BattleSprite();
    [GeneratedRegex("^FIGHT[0-9]{3}\\.(IDX|GRP)$")] private static partial Regex FightArchive();
    [GeneratedRegex("^E[0-9]{2}\\.WAV$")] private static partial Regex EffectAudio();
    [GeneratedRegex("^ATK[0-9]{2}\\.WAV$")] private static partial Regex AttackAudio();
    [GeneratedRegex("^GAME[0-9]{2}\\.XMI$")] private static partial Regex Music();
    [GeneratedRegex("^(ALLSIN|ALLDEF|RANGER|KDEF|TALK|HDGRP|MMAP|FMAP|WARFLD|EFT|CLOUD|FBK|TITLE|KEND|ENDWORD)\\.(IDX|GRP)$")]
    private static partial Regex Archive();
}
