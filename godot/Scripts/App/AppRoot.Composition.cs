using Godot;

namespace JinYong.Host;

public partial class AppRoot
{
    private void ComposeGameplayComponents(VBoxContainer side)
    {
        ComposeBattleComponents(side);
        ComposeShopComponents();
        ComposeEndingComponents();
        _musicController = new(_viewHost);
        ComposeMenus();
    }

    private void ComposeMenus()
    {
        var inventory = new InventoryPanel();
        AddChild(inventory);
        _inventoryController = new(_viewHost, inventory);
        var menu = new GameMenuPanel { LayoutTarget = _world };
        AddChild(menu);
        _menuController = new(_viewHost, menu);
        _world.MenuRequested += () => _menuController.OpenGameMenu();
        _menuController.InputAcknowledged += AcknowledgeMenuInput;
        _inventoryController.InputAcknowledged += AcknowledgeMenuInput;
        _inventoryController.Closed += () => _menuController.ResumeFromChild();
        _menuController.NewGameRequested += StartNewGame;
        _menuController.LoadRequested += () =>
        {
            OpenSaveMenu(false);
            if (!SaveMenuVisible) _menuController.ResumeFromChild("当前无法载入进度。");
        };
        _menuController.SaveRequested += () =>
        {
            OpenSaveMenu(true);
            if (!SaveMenuVisible) _menuController.ResumeFromChild("请等待行动结束后再保存。");
        };
        _menuController.InventoryRequested += () =>
        {
            if (!_inventoryController.Open()) _menuController.ResumeFromChild("当前无法打开行囊。");
        };
        _menuController.ResourcesRequested += ChooseFolder;
        _menuController.QuitRequested += () => GetTree().Quit();
        _menuController.PreferencesChanged += preferences =>
        {
            ApplyGamePreferences(preferences);
            if (_smoke) return;
            try { preferences.Save(); }
            catch (Exception error) { _menuController.ReportError(error.Message); GD.PushError(error.ToString()); }
        };
    }

    private void AcknowledgeMenuInput()
    {
        _heldDirections.Clear();
        _modalClosedFrame = Engine.GetProcessFrames();
        _world.ReadMovement([], false);
        BindStory(); _world.QueueRedraw();
    }

    private void ApplyGamePreferences(GamePreferences preferences)
    {
        _world.SmoothMovement = preferences.SmoothMovement;
        _smoothMovement.SetPressedNoSignal(preferences.SmoothMovement);
        _musicController.Enabled = preferences.MusicEnabled;
    }

    private void ShowInventory()
    {
        if (!SaveMenuVisible && !_folder.Visible) _inventoryController.Open();
    }

    private void ComposeBattleComponents(VBoxContainer side)
    {
        var actions = new BattleActionPanel { Name = "BattleControls" };
        side.AddChild(actions);
        var results = new BattleResultsPanel();
        AddChild(results);
        var audio = new BattleAudioPlayer { Name = "BattleAudio" };
        AddChild(audio);
        _battleController = new(_viewHost, actions, results, audio);
        _battleController.BattleModeChanged += active => _explorationPanel.Visible = _debugVisible && !active;
        _battleController.InputAcknowledged += () =>
        {
            _heldDirections.Clear();
            _modalClosedFrame = Engine.GetProcessFrames();
            BindStory();
        };
        AttachBattleDiagnostics(_battleController);
    }

    private void ComposeShopComponents()
    {
        var shop = new ShopPanel();
        AddChild(shop);
        _shopController = new(_viewHost, shop);
        _shopController.InputAcknowledged += () =>
        {
            _heldDirections.Clear();
            _modalClosedFrame = Engine.GetProcessFrames();
            BindStory();
            _world.QueueRedraw();
        };
    }

    private void ComposeEndingComponents()
    {
        var panel = new EndingPanel();
        AddChild(panel);
        _endingController = new(_viewHost, panel);
        _endingController.Completed += () => GetTree().Quit();
    }
}
