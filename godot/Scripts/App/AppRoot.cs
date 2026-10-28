using Godot;

namespace JinYong.Host;

public partial class AppRoot : Control
{
    private SceneView _world = null!;
    private GameViewHost _viewHost = null!;
    private BattleController _battleController = null!;
    private ShopController _shopController = null!;
    private EndingController _endingController = null!;
    private MusicController _musicController = null!;
    private bool _busy;
    private bool _finishing;

    public override void _Process(double delta)
    {
        _world.Advance(delta);
        _battleController.Advance(delta);
        _shopController.Advance();
        _endingController.Advance(delta);
        _musicController.Advance(delta);
        CompletePendingTravel();
        PresentLifecycleMenu();
        BindStory();
        if (_smoke && !_finishing) AdvanceSmoke();
        ReadPlayerInput();
        CompleteSmokeQuickTap();
    }

    public override void _ExitTree()
    {
        _battleController?.Dispose();
        _shopController?.Dispose();
        _endingController?.Dispose();
        _musicController?.Dispose();
        _audio.Stop();
        _audio.Stream = null;
        foreach (var stream in _audioStreams) stream.Dispose();
    }
}
