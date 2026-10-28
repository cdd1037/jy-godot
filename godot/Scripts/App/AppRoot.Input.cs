using Godot;
using JinYong.Core.Maps;
using JinYong.Core.Events;

namespace JinYong.Host;

public partial class AppRoot
{
    private readonly List<Direction> _heldDirections = [];

    private static string ActionName(Direction direction) => "walk_" + direction.ToString().ToLowerInvariant();
    private static void ConfigureInput()
    {
        (Direction Direction, Key Arrow, Key Letter)[] bindings =
        [(Direction.Up, Key.Up, Key.W), (Direction.Right, Key.Right, Key.D),
         (Direction.Left, Key.Left, Key.A), (Direction.Down, Key.Down, Key.S)];
        foreach (var (direction, arrow, letter) in bindings)
        {
            var action = ActionName(direction);
            if (InputMap.HasAction(action)) continue;
            InputMap.AddAction(action);
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = arrow });
            InputMap.ActionAddEvent(action, new InputEventKey { PhysicalKeycode = letter });
        }
        if (!InputMap.HasAction("interact"))
        {
            InputMap.AddAction("interact");
            foreach (var key in new[] { Key.E, Key.Space, Key.Enter, Key.KpEnter })
                InputMap.ActionAddEvent("interact", new InputEventKey { PhysicalKeycode = key });
        }
    }

    public override void _UnhandledKeyInput(InputEvent input)
    {
        if (_endingController.HandleKeyInput(input) || _shopController.HandleKeyInput(input) || _battleController.HandleKeyInput(input))
        {
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F1 })
        {
            _world.ShowCollision = !_world.ShowCollision;
            _world.QueueRedraw();
            GetViewport().SetInputAsHandled();
        }
        else if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F2 })
        {
            _smoothMovement.ButtonPressed = !_smoothMovement.ButtonPressed;
            GetViewport().SetInputAsHandled();
        }
    }

    private void ReadPlayerInput()
    {
        _heldDirections.RemoveAll(direction => !Input.IsActionPressed(ActionName(direction)));
        foreach (var direction in Enum.GetValues<Direction>())
            if ((Input.IsActionPressed(ActionName(direction)) || Input.IsActionJustPressed(ActionName(direction))) &&
                !_heldDirections.Contains(direction)) _heldDirections.Add(direction);
        var typing = GetViewport().GuiGetFocusOwner() is LineEdit;
        var canWalk = !_finishing && !_busy && !typing && !_folder.Visible && !SaveMenuVisible && !_shopController.IsOpen && !_endingController.IsOpen && !_battleController.ResultsOpen && _inventoryDialog?.Visible != true && _modalClosedFrame != Engine.GetProcessFrames();
        if (canWalk)
        {
            if (_world.Session?.IsEventRunning == true)
            {
                canWalk = false;
                if (_world.Session.Events?.Pending?.Kind == EventPresentationKind.Choice)
                {
                    if (Input.IsActionJustPressed("walk_up")) _dialogue.MoveChoice(-1);
                    else if (Input.IsActionJustPressed("walk_down")) _dialogue.MoveChoice(1);
                    if (Input.IsActionJustPressed("interact")) _dialogue.ConfirmSelection();
                    else if (Input.IsActionJustPressed("ui_cancel")) _dialogue.CancelSelection();
                }
                else if (Input.IsActionJustPressed("interact") && _dialogue.Token is { } token) ConfirmDialogue(token);
            }
            else if (_world.Session?.IsInputLocked == true) canWalk = false;
            else if (!_world.IsMoving && Input.IsActionJustPressed("interact"))
            {
                canWalk = false;
                if (!_world.Interact() && _world.Session?.InteractionMessage is { } message) _status.Text = message;
                BindStory();
            }
        }
        _world.ReadMovement(_heldDirections, canWalk);
    }
}
