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
        _battleController.BattleModeChanged += active => _explorationPanel.Visible = !active;
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
