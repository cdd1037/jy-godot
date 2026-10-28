using System.Security.Cryptography;

namespace JinYong.Content.Legacy;

public sealed record LegacyScriptAudit(int Id, int ByteLength, bool CompilerAccepted, int CommandCount,
    int[] SourceOpcodes, string[] Operations, string? FailureReason)
{
    // Compiling a complete control-flow graph is necessary, but does not verify
    // content references, host services, or every state-dependent runtime path.
    public bool ExecutionVerified => false;
}

public sealed record LegacyEventAuditReport(int FormatVersion, string Dialect, int ArchiveEntryCount,
    int EmptyEntryCount, IReadOnlyDictionary<string, string> SourceHashes, LegacyScriptAudit[] Scripts)
{
    public int CompilerAcceptedCount => Scripts.Count(script => script.CompilerAccepted);
    public int RejectedCount => Scripts.Length - CompilerAcceptedCount;
    public bool KnownOriginalArchive =>
        SourceHashes.GetValueOrDefault("KDEF.IDX") == LegacyEventAudit.OriginalIndexSha256 &&
        SourceHashes.GetValueOrDefault("KDEF.GRP") == LegacyEventAudit.OriginalGroupSha256;
    public bool ExecutionVerified => false;
    public string Scope => "Full-script compiler and command validation only; content dependencies, host capabilities, and runtime paths require separate verification.";
}

/// <summary>
/// Read-only audit of every nonempty KDEF record. Unsupported instructions reject
/// the whole script; no decoder-only result is counted as compiler acceptance.
/// DOS widths and dispatch contract: OpenLegend research/evidence/functions/Z_DAT/0x2C319.md.
/// </summary>
public static class LegacyEventAudit
{
    public const string OriginalIndexSha256 = "bf02eccf1dab01fd0ae033cdf807f56b978a71a6c2ab9be05e366239b485104b";
    public const string OriginalGroupSha256 = "135c5e097a7fe561ee931046e1bebf55de9b469678e3d111d8e9f2c6bb600e06";

    public static LegacyEventAuditReport Read(string sourceDirectory, LegacyScriptDialect dialect = LegacyScriptDialect.DosOriginal)
    {
        // Extended opcode 50 has no reliable byte signature. Its dialect must be
        // declared by the caller, never guessed from arguments resembling DOS 50.
        if (dialect != LegacyScriptDialect.DosOriginal)
            throw new NotSupportedException($"KDEF dialect {dialect} is not supported; extended opcode 50 must not be interpreted as DOS opcode 50.");
        var files = Directory.GetFiles(Path.GetFullPath(sourceDirectory))
            .ToDictionary(path => Path.GetFileName(path)!, path => path, StringComparer.OrdinalIgnoreCase);
        byte[] ReadResource(string name) => files.TryGetValue(name, out var path)
            ? File.ReadAllBytes(path) : throw new FileNotFoundException($"Missing original resource: {name}", name);
        var index = ReadResource("KDEF.IDX");
        var group = ReadResource("KDEF.GRP");
        var archive = LegacyArchive.Parse(index, group);
        var hashes = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["KDEF.IDX"] = Convert.ToHexStringLower(SHA256.HashData(index)),
            ["KDEF.GRP"] = Convert.ToHexStringLower(SHA256.HashData(group))
        };
        var scripts = new List<LegacyScriptAudit>();
        var empty = 0;
        for (var id = 0; id < archive.Count; id++)
        {
            var bytes = archive.Entry(id);
            if (bytes.IsEmpty) { empty++; continue; }
            try
            {
                var program = LegacyEventCompiler.Compile(id, bytes);
                scripts.Add(new(id, bytes.Length, true, program.Commands.Length,
                    program.Commands.Select(command => command.SourceOpcode).Where(opcode => opcode >= 0).Distinct().Order().ToArray(),
                    program.Commands.Select(command => command.Operation.ToString()).Distinct().Order(StringComparer.Ordinal).ToArray(), null));
            }
            catch (Exception error) when (error is InvalidDataException or NotSupportedException or ArgumentException or OverflowException)
            {
                // Deliberately omit partial command/opcode counts: a failed
                // script must not look supported merely because a prefix decoded.
                scripts.Add(new(id, bytes.Length, false, 0, [], [], error.Message));
            }
        }
        return new(1, "dos_original", archive.Count, empty, hashes, scripts.ToArray());
    }
}
