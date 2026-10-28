using Godot;

namespace JinYong.Host;

public partial class AppRoot
{
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
}
