using Godot;
using JinYong.Content.Packages;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class SceneView : Control
{
    private sealed record Frame(Texture2D Texture, Rect2 Region, Vector2 Offset);
    private Dictionary<int, Frame> _frames = [];
    private readonly StringComparer _pathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    // Absolute atlas paths include their library root. Only the active library's
    // textures are retained, shared by scene frames and portrait atlas wrappers.
    private Dictionary<string, ImageTexture> _textures = [];
    private string? _textureLibraryRoot;
    private Dictionary<int, AtlasTexture> _portraits = [];
    private GameSession? _session;
    private SceneWalker? _walker;
    private float _zoom = 2;
    private readonly SystemFont _font = new() { FontNames = ["Microsoft YaHei", "Noto Sans CJK SC"] };

    public event Action<MovementResult>? MovementAttempted;
    public bool IsMoving => _walker?.IsMoving == true;
    public bool ShowCollision { get; set; }
    public bool SmoothMovement { get; set; } = true;
    public bool PlayOpening { get; set; }
    public float Zoom => _zoom;
    public GameSession? Session => _session;
    public int DrawnSprites { get; private set; }

    public override void _Ready()
    {
        ClipContents = true;
        TextureFilter = TextureFilterEnum.Nearest;
        MouseFilter = MouseFilterEnum.Stop;
        Resized += QueueRedraw;
    }

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
        var portraits = new Dictionary<int, AtlasTexture>();
        GameSession session;
        SceneWalker walker;
        try
        {
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
        _textures = textures;
        _textureLibraryRoot = root;
        _session = session;
        _walker = walker;
        foreach (var portrait in oldPortraits.Values) portrait.Dispose();
        if (!reuseTextures)
            foreach (var texture in oldTextures.Values) texture.Dispose();
        QueueRedraw();
    }

    public Texture2D? Portrait(int id) => _portraits.GetValueOrDefault(id);

    public void Advance(double delta)
    {
        if (_session is null) return;
        var revision = _session.Scene.Revision + _session.PresentationRevision;
        _session.Advance(TimeSpan.FromSeconds(delta));
        var wasMoving = IsMoving;
        _walker!.Advance(delta);
        if (revision != _session.Scene.Revision + _session.PresentationRevision) QueueRedraw();
        if (wasMoving) QueueRedraw();
    }

    public void ReadMovement(IReadOnlyList<Direction> held, bool enabled)
    {
        if (_walker is null || _session is null) return;
        var revision = _session.PresentationRevision;
        var result = _walker.ReadInput(held, enabled);
        if (revision != _session.PresentationRevision) QueueRedraw();
        if (result is { } movement) MovementAttempted?.Invoke(movement);
    }

    public bool Interact() => _session is not null && !IsMoving && _session.Interact();

    public override void _GuiInput(InputEvent input)
    {
        if (TryHandleBattleClick(input)) { AcceptEvent(); return; }
        if (input is not InputEventMouseButton { Pressed: true } mouse) return;
        if (mouse.ButtonIndex == MouseButton.WheelUp) _zoom = Math.Min(4, _zoom + 0.25f);
        else if (mouse.ButtonIndex == MouseButton.WheelDown) _zoom = Math.Max(1, _zoom - 0.25f);
        else return;
        QueueRedraw();
        AcceptEvent();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("101c1a"));
        if (_session is null)
        {
            for (var y = 20; y < Size.Y; y += 32)
            for (var x = 20; x < Size.X; x += 32)
                DrawCircle(new Vector2(x, y), 0.65f, new Color("263c35"));
            var text = "江湖待启";
            var width = _font.GetStringSize(text, fontSize: 34).X;
            DrawString(_font, Size / 2 - new Vector2(width / 2, 12), text, fontSize: 34, modulate: new Color("c9b992"));
            text = "选择原版游戏目录，进入熟悉的江湖";
            width = _font.GetStringSize(text, fontSize: 15).X;
            DrawString(_font, Size / 2 + new Vector2(-width / 2, 26), text, fontSize: 15, modulate: new Color("83978b"));
            return;
        }

        if (_session.Battle is not null) { DrawBattle(); return; }
        if (_session.IsWorld)
        {
            DrawWorld();
            if (_session.FadeOpacity > 0)
                DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, (float)_session.FadeOpacity));
            return;
        }
        var scene = _session.Scene;
        var player = _session.Player;
        Vector2 Project(GridPosition point, int height = 0)
        {
            var (x, y) = IsometricProjection.Project(point, height);
            return new Vector2(x, y);
        }
        var motion = _session.ScriptedMotion;
        var fromCell = motion?.From ?? _walker!.From;
        var toCell = motion?.To ?? _walker!.To;
        if (motion is null && !_walker!.IsMoving) fromCell = toCell = player.Position;
        var progress = SmoothMovement ? motion?.Progress ?? _walker!.Progress : 1;
        var from = Project(fromCell, scene.At(MapLayer.BuildingHeight, fromCell));
        var to = Project(toCell, scene.At(MapLayer.BuildingHeight, toCell));
        var actor = from.Lerp(to, (float)progress);
        var camera = _session.Camera is { } view
            ? new Vector2((float)((view.X - view.Y) * 18), (float)((view.X + view.Y) * 9)) : actor;
        var origin = Size / 2 - camera * _zoom;
        DrawnSprites = 0;
        void Sprite(int id, Vector2 anchor)
        {
            if (!_frames.TryGetValue(id, out var frame)) return;
            var rectangle = new Rect2(origin + (anchor - frame.Offset) * _zoom, frame.Region.Size * _zoom);
            if (!rectangle.Intersects(new Rect2(Vector2.Zero, Size))) return;
            DrawTextureRectRegion(frame.Texture, rectangle, frame.Region);
            DrawnSprites++;
        }

        // Preserve OpenLegend's x-major two-pass draw order, offsets and height
        // layers. Sorting everything solely by screen Y breaks tall structures.
        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
        {
            var point = new GridPosition(x, y);
            if (scene.At(MapLayer.BuildingHeight, point) == 0)
                Sprite(scene.At(MapLayer.Ground, point), Project(point));
        }
        var actorCell = progress < 0.5 ? fromCell : toCell;
        var firstFrame = new[] { 5002, 5016, 5030, 5044 }[(int)player.Facing];
        for (var x = 0; x < 64; x++)
        for (var y = 0; y < 64; y++)
        {
            var point = new GridPosition(x, y);
            var anchor = Project(point);
            var height = scene.At(MapLayer.BuildingHeight, point);
            if (height != 0) Sprite(scene.At(MapLayer.Ground, point), anchor);
            var building = scene.At(MapLayer.Building, point);
            if (building is not 0 and not 15000) Sprite(building, anchor - new Vector2(0, height));
            if (scene.EventAt(point) is { Picture: > 0 } pointEvent)
                Sprite(pointEvent.Picture, anchor - new Vector2(0, height));
            if (point == actorCell) Sprite(player.PictureOverride ?? (firstFrame + player.WalkFrameOffset), actor);
            var decoration = scene.At(MapLayer.Decoration, point);
            if (decoration != 0) Sprite(decoration, anchor - new Vector2(0, scene.At(MapLayer.DecorationHeight, point)));
        }
        if (ShowCollision)
        {
            for (var x = Math.Max(0, player.Position.X - 14); x <= Math.Min(63, player.Position.X + 14); x++)
            for (var y = Math.Max(0, player.Position.Y - 14); y <= Math.Min(63, player.Position.Y + 14); y++)
            {
                var point = new GridPosition(x, y);
                var center = origin + Project(point, scene.At(MapLayer.BuildingHeight, point)) * _zoom;
                var block = SceneMovementRules.Check(scene, point, point);
                var color = block == MovementBlock.None ? new Color(0.5f, 0.8f, 0.55f, 0.25f) : new Color(1, 0.4f, 0.25f, 0.7f);
                DrawPolyline([center + new Vector2(-18, 0) * _zoom, center + new Vector2(0, -9) * _zoom,
                    center + new Vector2(18, 0) * _zoom, center + new Vector2(0, 9) * _zoom,
                    center + new Vector2(-18, 0) * _zoom], color);
            }
        }
        if (_session.FadeOpacity > 0)
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, (float)_session.FadeOpacity));
    }

    public override void _ExitTree()
    {
        foreach (var portrait in _portraits.Values) portrait.Dispose();
        foreach (var texture in _textures.Values) texture.Dispose();
        _portraits.Clear();
        _frames.Clear();
        _textures.Clear();
        _textureLibraryRoot = null;
        _font.Dispose();
    }
}
