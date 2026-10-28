using Godot;

namespace JinYong.Host;

public partial class AppRoot : Control
{
    private SceneView _world = null!;
    private bool _busy;
    private bool _finishing;

    public override void _Process(double delta)
    {
        _world.Advance(delta);
        AdvanceBattleHost(delta);
        CompletePendingTravel();
        PresentLifecycleMenu();
        BindStory();
        if (_smoke && !_finishing) AdvanceSmoke();
        ReadPlayerInput();
        CompleteSmokeQuickTap();
    }

    public override void _ExitTree()
    {
        StopBattleAudio(releaseCachedStreams: true);
        _audio.Stop();
        _audio.Stream = null;
        foreach (var stream in _audioStreams) stream.Dispose();
    }
}
