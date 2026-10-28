namespace JinYong.Content.Packages;

public sealed record PackageManifest
{
    public int FormatVersion { get; init; } = 1;
    public string Id { get; init; } = "base";
    public string EngineApi { get; init; } = "1";
    public string Version { get; init; } = "0.1.0";
    public string Scene { get; init; } = "scene.json";
    public Dictionary<string, string> SourceHashes { get; init; } = [];
    public Dictionary<string, string> FileHashes { get; init; } = [];
}
