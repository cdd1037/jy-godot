using JinYong.Content.Library;
using JinYong.Core.Events;
using JinYong.Core.Maps;

namespace JinYong.Content.Packages;

public sealed record ScenePackage(string Directory, PackageManifest Manifest, SceneDocument Document, SceneDefinition Scene, StoryCatalog? Story = null)
{
    public string Resolve(string relativePath) => ScenePackageReader.Resolve(Directory, relativePath);
    public GameLibrary? Library { get; init; }
}
