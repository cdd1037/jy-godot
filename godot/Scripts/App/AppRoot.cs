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
    private GameMenuController _menuController = null!;
    private InventoryController _inventoryController = null!;
    private ulong? _modalClosedFrame;
    private bool _directPlay, _debugVisible;
    private bool _busy;
    private bool _finishing;

    public override void _Process(double delta)
    {
        _menuController.Advance();
        _inventoryController.Advance();
        if (!_menuController.BlocksSimulation && !_inventoryController.IsOpen && !SaveMenuVisible && !_folder.Visible && !_busy)
        {
            _world.Advance(delta);
            _battleController.Advance(delta);
        }
        _shopController.Advance();
        _endingController.Advance(delta);
        _musicController.Advance(delta);
        CompletePendingTravel();
        PresentLifecycleMenu();
        BindStory();
        _sidePanel.Visible = _debugVisible || _world.Session?.Battle is not null;
        _gameHeader.Visible = _debugVisible;
        _gameFooter.Visible = _debugVisible || _busy;
        _explorationPanel.Visible = _debugVisible && _world.Session?.Battle is null;
        _menuButton.Disabled = _busy || _menuController.BlocksSimulation || _inventoryController.IsOpen || SaveMenuVisible ||
            _world.Session?.CanSave != true || _world.IsMoving;
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
        _menuController?.Dispose();
        _inventoryController?.Dispose();
        _audio.Stop();
        _audio.Stream = null;
        foreach (var stream in _audioStreams) stream.Dispose();
    }
}
