using Godot;
using JinYong.Content.Packages;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class SceneView
{
    private sealed record Frame(Texture2D Texture, Rect2 Region, Vector2 Offset);
    private Dictionary<int, Frame> _frames = [];
    private Dictionary<string, Dictionary<int,Frame?>> _battleFrames = new(StringComparer.Ordinal);
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    // Absolute atlas paths include their library root. Only the active library's
    // textures are retained, shared by scene frames and portrait atlas wrappers.
    private Dictionary<string, ImageTexture> _textures = [];
    private string? _textureLibraryRoot;
    private Dictionary<int, AtlasTexture> _portraits = [];
    private readonly SystemFont _font = new() { FontNames = ["Microsoft YaHei", "Noto Sans CJK SC"] };

    public void LoadPackage(ScenePackage package, GameSession? retainedSession = null, long? travelToken = null)
    {
        // Borrow the active library's cache while constructing the new view.
        // A failed view must dispose only resources created during this attempt.
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(package.Directory));
        var reuseTextures = _pathComparer.Equals(root, _textureLibraryRoot);
        var textures = reuseTextures
            ? new Dictionary<string, ImageTexture>(_textures, _pathComparer)
            : new Dictionary<string, ImageTexture>(_pathComparer);
        var createdTextures = new List<ImageTexture>();
        var createdPortraits = new List<AtlasTexture>();
        var frames = new Dictionary<int, Frame>();
        var battleFrames = new Dictionary<string,Dictionary<int,Frame?>>(StringComparer.Ordinal);
        Color[] battlePalette = [];
        var portraits = new Dictionary<int, AtlasTexture>();
        GameSession session;
        SceneWalker walker;
        try
        {
            if(package.Document.BattleSpriteBanks.Count>0)
            {
                var rgb=JinYong.Content.Imaging.VgaPalette.DecodeRgb6(File.ReadAllBytes(package.Resolve(package.Document.Palette)));
                battlePalette=Enumerable.Range(0,256).Select(index=>new Color(rgb[3*index]/255f,rgb[3*index+1]/255f,rgb[3*index+2]/255f)).ToArray();
            }
            ImageTexture Texture(string atlas)
            {
                var path = package.Resolve(atlas);
                if (textures.TryGetValue(path, out var existing)) return existing;
                using var image = Image.LoadFromFile(path);
                if (image is null || image.IsEmpty()) throw new InvalidDataException($"Unable to load atlas {atlas}.");
                var texture = ImageTexture.CreateFromImage(image);
                createdTextures.Add(texture);
                textures.Add(path, texture);
                return texture;
            }
            foreach (var sprite in package.Document.Sprites)
            {
                if (sprite.Width == 0) continue;
                var texture = Texture(sprite.Atlas);
                if (sprite.X + sprite.Width > texture.GetWidth() || sprite.Y + sprite.Height > texture.GetHeight())
                    throw new InvalidDataException($"Sprite {sprite.LegacyId} is outside atlas bounds.");
                frames.Add(sprite.LegacyId, new Frame(texture,
                    new Rect2(sprite.X, sprite.Y, sprite.Width, sprite.Height), new Vector2(sprite.OffsetX, sprite.OffsetY)));
            }
            foreach (var (bank,sprites) in package.Document.BattleSpriteBanks)
            {
                var entries = new Dictionary<int,Frame?>();
                foreach(var sprite in sprites)
                {
                    // Empty decoded frames are valid no-draw entries, not missing
                    // banks and not a reason to hide unrelated combatants.
                    if(sprite.Width==0) { entries.Add(sprite.LegacyId,null); continue; }
                    var texture=Texture(sprite.Atlas);
                    if(sprite.X+sprite.Width>texture.GetWidth() || sprite.Y+sprite.Height>texture.GetHeight())
                        throw new InvalidDataException($"Battle animation sprite {bank}:{sprite.LegacyId} is outside atlas bounds.");
                    entries.Add(sprite.LegacyId,new Frame(texture,new Rect2(sprite.X,sprite.Y,sprite.Width,sprite.Height),
                        new Vector2(sprite.OffsetX,sprite.OffsetY)));
                }
                battleFrames.Add(bank,entries);
            }
            foreach (var sprite in package.Document.Portraits)
            {
                if (sprite.Width == 0) continue;
                var texture = Texture(sprite.Atlas);
                if (sprite.X + sprite.Width > texture.GetWidth() || sprite.Y + sprite.Height > texture.GetHeight())
                    throw new InvalidDataException("Portrait is outside its atlas.");
                var portrait = new AtlasTexture();
                createdPortraits.Add(portrait);
                portrait.Atlas = texture;
                portrait.Region = new Rect2(sprite.X, sprite.Y, sprite.Width, sprite.Height);
                portraits.Add(sprite.LegacyId, portrait);
            }
            session = retainedSession ?? new GameSession(package.Scene, package.Story, skipOpening: !PlayOpening,
                worldMap: package.Library?.MainMap, travelStart: package.Library?.GameData.TravelStart,
                battles: package.Library?.Battles);
            if (travelToken is { } token && !session.CommitTravel(token))
                throw new InvalidOperationException("The scene transition is no longer ready.");
            walker = new SceneWalker(session);
        }
        catch
        {
            foreach (var portrait in createdPortraits) portrait.Dispose();
            foreach (var texture in createdTextures) texture.Dispose();
            throw;
        }

        // Commit once all textures, wrappers and game state are valid. An old
        // library can be released only after a successful replacement is ready.
        var oldPortraits = _portraits;
        var oldTextures = _textures;
        _portraits = portraits;
        _frames = frames;
        _battleFrames = battleFrames;
        _battlePalette = battlePalette;
        _missingBattleSpriteMessages.Clear();
        _textures = textures;
        _textureLibraryRoot = root;
        _session = session;
        _walker = walker;
        foreach (var portrait in oldPortraits.Values) portrait.Dispose();
        if (!reuseTextures)
        {
            foreach(var texture in _battleFlashTextures.Values) texture.Dispose();
            _battleFlashTextures.Clear();
            foreach (var texture in oldTextures.Values) texture.Dispose();
        }
        QueueRedraw();
    }

    public Texture2D? Portrait(int id) => _portraits.GetValueOrDefault(id);

    private void ReleaseAssets()
    {
        foreach (var portrait in _portraits.Values) portrait.Dispose();
        foreach (var texture in _textures.Values) texture.Dispose();
        _portraits.Clear();
        _frames.Clear();
        _battleFrames.Clear();
        foreach(var texture in _battleFlashTextures.Values) texture.Dispose();
        _battleFlashTextures.Clear();
        _textures.Clear();
        _textureLibraryRoot = null;
        _font.Dispose();
    }
}
