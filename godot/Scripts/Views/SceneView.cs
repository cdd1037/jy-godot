using Godot;
using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class SceneView : Control
{
    private GameSession? _session;
    private SceneWalker? _walker;
    private float _zoom = 2;

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
        MouseExited += ClearBattleHover;
    }

    public void Advance(double delta)
    {
        if (_session is null) return;
        var wasPresenting = _session.Battle?.IsPresenting == true;
        var revision = _session.Scene.Revision + _session.PresentationRevision;
        _session.Advance(TimeSpan.FromSeconds(delta));
        var wasMoving = IsMoving;
        _walker!.Advance(delta);
        if (revision != _session.Scene.Revision + _session.PresentationRevision) QueueRedraw();
        if (wasMoving) QueueRedraw();
        if (wasPresenting || _session.Battle?.IsPresenting == true) QueueRedraw();
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
        if (TryHandleBattleHover(input)) { AcceptEvent(); return; }
        if (TryHandleBattleClick(input)) { AcceptEvent(); return; }
        if (input is not InputEventMouseButton { Pressed: true } mouse) return;
        if (mouse.ButtonIndex == MouseButton.WheelUp) _zoom = Math.Min(4, _zoom + 0.25f);
        else if (mouse.ButtonIndex == MouseButton.WheelDown) _zoom = Math.Max(1, _zoom - 0.25f);
        else return;
        if (_session?.Battle is not null) UpdateBattleHover(mouse.Position);
        QueueRedraw();
        AcceptEvent();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), new Color("101c1a"));
        if (_session is null) { DrawWelcome(); return; }
        if (_session.Battle is not null) { DrawBattle(); return; }
        if (_session.IsWorld)
        {
            DrawWorld();
            if (_session.FadeOpacity > 0)
                DrawRect(new Rect2(Vector2.Zero, Size), new Color(0, 0, 0, (float)_session.FadeOpacity));
            return;
        }
        DrawExploration();
    }

    public override void _ExitTree() => ReleaseAssets();
}
