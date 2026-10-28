using System.Collections.ObjectModel;
using JinYong.Content.Packages;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Battles;

namespace JinYong.Content.Library;

/// <summary>Owns shared definitions and file locations. OpenScene creates a view, never a new export.</summary>
public sealed partial class GameLibrary
{
    private readonly StoryCatalog _definitions;
    private GameLibrary(string directory, GameLibraryManifest manifest, LibraryScene[] scenes, GameDataDocument gameplay,
        WorldDefinition world, SharedSpriteCatalog sprites, LibraryAudio audio, Dictionary<string, SourceResource> sources,
        StoryCatalog definitions, WorldMapDefinition? mainMap, BattleCatalog? battles)
    {
        Directory = directory; Manifest = manifest; Scenes = Array.AsReadOnly(scenes); GameData = gameplay;
        World = world; Sprites = sprites; Audio = audio;
        Sources = new ReadOnlyDictionary<string, SourceResource>(sources);
        _definitions = definitions;
        MainMap = mainMap;
        Battles = battles;
    }

    public string Directory { get; }
    public GameLibraryManifest Manifest { get; }
    public IReadOnlyList<LibraryScene> Scenes { get; }
    public GameDataDocument GameData { get; }
    public WorldDefinition World { get; }
    public WorldMapDefinition? MainMap { get; }
    public BattleCatalog? Battles { get; }
    public SharedSpriteCatalog Sprites { get; }
    public LibraryAudio Audio { get; }
    public IReadOnlyDictionary<string, SourceResource> Sources { get; }
    public string Resolve(string path) => ScenePackageReader.Resolve(Directory, path);
}
