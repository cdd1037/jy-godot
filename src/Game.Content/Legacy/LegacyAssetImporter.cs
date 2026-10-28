using System.Buffers.Binary;
using System.Security.Cryptography;
using JinYong.Content.Library;
using JinYong.Content.Packages;

namespace JinYong.Content.Legacy;

/// <summary>
/// Imports every scene/portrait/world frame into one deduplicated atlas collection.
/// OpenLegend references: resource/packed_archive.cpp for SDX aliases/sentinel,
/// resource/legacy_sprite.cpp for decoded geometry/RLE; portraits and MMAP use ordinary IDX.
/// No original resources or per-scene archives are copied by this importer.
/// </summary>
public static class LegacyAssetImporter
{
    public static SharedSpriteCatalog Import(Func<string, byte[]> read, IReadOnlyCollection<string> filenames,
        string stagingDirectory, byte[] palette, int requiredSceneCount, IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(filenames);
        ArgumentNullException.ThrowIfNull(palette);
        if (palette.Length != 768) throw new ArgumentException("Atlas palette must contain 256 RGB8 colors.", nameof(palette));
        if (requiredSceneCount is < 0 or > 1000) throw new ArgumentOutOfRangeException(nameof(requiredSceneCount));
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filename in filenames)
        {
            if (string.IsNullOrWhiteSpace(filename) || Path.GetFileName(filename) != filename)
                throw new ArgumentException("Resource filenames must be base names, not paths.", nameof(filenames));
            if (!names.TryAdd(filename, filename)) throw new InvalidDataException($"Duplicate resource filename: {filename}.");
        }
        string Require(string filename) => names.TryGetValue(filename, out var actual) ? actual :
            throw new FileNotFoundException($"Missing original resource: {filename}", filename);
        for (var id = 0; id < requiredSceneCount; id++) { _ = Require($"SDX{id:D3}"); _ = Require($"SMP{id:D3}"); }
        _ = Require("HDGRP.IDX");
        _ = Require("HDGRP.GRP");
        // Synthetic scene-only fixtures may omit the optional world pair entirely.
        // An incomplete pair is always an error, never an apparently successful import.
        var hasWorld = names.ContainsKey("MMAP.IDX") || names.ContainsKey("MMAP.GRP");
        if (hasWorld) { _ = Require("MMAP.IDX"); _ = Require("MMAP.GRP"); }

        static bool IsSceneFile(string filename, string prefix) => filename.Length == 6 &&
            filename.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            filename.AsSpan(3).IndexOfAnyExceptInRange('0', '9') == -1;
        var sceneIds = names.Keys.Where(name => IsSceneFile(name, "SDX") || IsSceneFile(name, "SMP"))
            .Select(name => int.Parse(name.AsSpan(3), System.Globalization.CultureInfo.InvariantCulture)).Distinct().Order().ToArray();
        // Validate every discovered pair before decoding or writing any output.
        foreach (var id in sceneIds) { _ = Require($"SDX{id:D3}"); _ = Require($"SMP{id:D3}"); }

        var encodedToAsset = new Dictionary<string, string>(StringComparer.Ordinal);
        var assetOrdinals = new Dictionary<string, int>(StringComparer.Ordinal);
        var unique = new Dictionary<int, IndexedSprite>();
        var banks = new Dictionary<string, Dictionary<int, string>>(StringComparer.Ordinal);
        string Frame(ReadOnlySpan<byte> bytes)
        {
            var encodedHash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (encodedToAsset.TryGetValue(encodedHash, out var existing)) return existing;
            var sprite = LegacySpriteDecoder.Decode(bytes);
            var hash = DecodedHash(sprite);
            if (!assetOrdinals.ContainsKey(hash))
            {
                var ordinal = unique.Count;
                assetOrdinals.Add(hash, ordinal);
                unique.Add(ordinal, sprite);
            }
            encodedToAsset.Add(encodedHash, hash);
            return hash;
        }
        void Bank(string bank, LegacyArchive archive, bool doubledIds, bool aliasEmpty = false)
        {
            var mapping = new Dictionary<int, string>();
            for (var index = 0; index < archive.Count; index++)
            {
                var localId = doubledIds ? checked(index * 2) : index;
                try { mapping.Add(localId, Frame(archive.Entry(index, aliasEmpty))); }
                catch (InvalidDataException error) { throw new InvalidDataException($"Sprite bank {bank}, frame {localId}: {error.Message}", error); }
            }
            banks.Add(bank, mapping);
        }

        for (var sceneIndex = 0; sceneIndex < sceneIds.Length; sceneIndex++)
        {
            var id = sceneIds[sceneIndex];
            progress?.Report($"聚合场景图像 {sceneIndex + 1}/{sceneIds.Length}：SMP{id:D3}");
            var archive = LegacyArchive.Parse(read(Require($"SDX{id:D3}")), read(Require($"SMP{id:D3}")), sentinel: true);
            Bank($"scene/{id:D3}", archive, doubledIds: true, aliasEmpty: true);
        }
        progress?.Report("聚合全部人物头像…");
        Bank("portrait", LegacyArchive.Parse(read(Require("HDGRP.IDX")), read(Require("HDGRP.GRP"))), doubledIds: false);
        if (hasWorld)
        {
            progress?.Report("聚合全部世界地图图像、主角和船帧…");
            // WorldSession::draw_sprite divides the stored legacy ID by two,
            // but uses PackedArchive::entry, not SDX legacy_pointer_entry.
            Bank("world", LegacyArchive.Parse(read(Require("MMAP.IDX")), read(Require("MMAP.GRP"))), doubledIds: true);
        }
        progress?.Report($"写入共享图集：{unique.Count} 个唯一图像，{banks.Sum(pair => pair.Value.Count)} 个本地编号…");
        // Palette has already been expanded with LegacySpriteDecoder.ReadPalette.
        // One writer call packs all unique frames, never a separate atlas per scene.
        var packed = SpriteAtlasWriter.Write(stagingDirectory, unique, palette, "assets/sprites");
        var assets = new Dictionary<string, SpriteAsset>(StringComparer.Ordinal);
        foreach (var (hash, ordinal) in assetOrdinals) assets.Add(hash, packed[ordinal]);
        return new SharedSpriteCatalog { Assets = assets, Banks = banks };
    }

    // Stable hash format: four little-endian int32s (width, height, offsetX, offsetY),
    // then width*height index bytes, then the same number of independent alpha bytes.
    // Anchors and coverage must participate: palette index zero can be opaque.
    private static string DecodedHash(IndexedSprite sprite)
    {
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteInt32LittleEndian(header, sprite.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], sprite.Height);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..], sprite.OffsetX);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..], sprite.OffsetY);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(header);
        hash.AppendData(sprite.Indices);
        hash.AppendData(sprite.Alpha);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
