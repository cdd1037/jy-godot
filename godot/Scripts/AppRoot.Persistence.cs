using Godot;
using JinYong.Content.Persistence;
using JinYong.Core.Events;

namespace JinYong.Host;

public partial class AppRoot
{
    private Button _saveButton = null!, _loadButton = null!, _saveCancel = null!, _saveQuit = null!;
    private Control _savePanel = null!;
    private PanelContainer _saveCard = null!;
    private Label _saveTitle = null!, _saveHint = null!, _saveError = null!;
    private readonly Button[] _saveSlots = new Button[3];
    private ConfirmationDialog _saveConfirmation = null!;
    private Action? _saveConfirmedAction;
    private bool _saving;
    private EventLifecycleRequest? _shownLifecycle;
    private string _savesDirectory = "";
    private bool SaveMenuVisible => _savePanel?.Visible == true;

    private SaveGameStore SaveStore() => new(_savesDirectory,
        _package?.Library is { } library ? [library.Directory] : []);

    private bool CanOpenSaveMenu => !_busy && _package?.Library is not null &&
        _world.Session is { CanSave: true } && !_world.IsMoving && !SaveMenuVisible &&
        _inventoryDialog is null && !_folder.Visible;

    private void BuildSaveButtons(VBoxContainer side)
    {
        var row = new HBoxContainer();
        _saveButton = new Button { Name = "SaveGame", Text = "保存进度", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None, Disabled = true };
        _loadButton = new Button { Name = "LoadGame", Text = "载入进度", SizeFlagsHorizontal = SizeFlags.ExpandFill,
            FocusMode = FocusModeEnum.None, Disabled = true };
        _saveButton.Pressed += () => OpenSaveMenu(true);
        _loadButton.Pressed += () => OpenSaveMenu(false);
        row.AddChild(_saveButton); row.AddChild(_loadButton); side.AddChild(row);
    }

    private void BuildSaveMenu()
    {
        _savePanel = new Control { Name = "SaveMenu", Visible = false, MouseFilter = MouseFilterEnum.Stop };
        _savePanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_savePanel);
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.94f), MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _savePanel.AddChild(veil);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _savePanel.AddChild(center);
        _saveCard = new PanelContainer { CustomMinimumSize = new Vector2(560, 0) };
        center.AddChild(_saveCard);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 14); _saveCard.AddChild(column);
        _saveTitle = new Label { Text = "载入进度" }; _saveTitle.AddThemeFontSizeOverride("font_size", 28); column.AddChild(_saveTitle);
        _saveHint = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart }; column.AddChild(_saveHint);
        for (var i = 0; i < _saveSlots.Length; i++)
        {
            var slot = i + 1;
            var button = new Button { Name = $"Slot{slot}", CustomMinimumSize = new Vector2(520, 64), Alignment = HorizontalAlignment.Left,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
            button.Pressed += () => SelectSaveSlot(slot);
            column.AddChild(button); _saveSlots[i] = button;
        }
        _saveError = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("e6b89d") };
        column.AddChild(_saveError);
        var actions = new HBoxContainer(); column.AddChild(actions);
        _saveCancel = new Button { Name = "Cancel", Text = "返回游戏", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _saveCancel.Pressed += CloseSaveMenu;
        _saveQuit = new Button { Name = "Quit", Text = "退出游戏", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _saveQuit.Pressed += () => ConfirmSaveAction("退出游戏", "确定退出游戏？", () => GetTree().Quit());
        actions.AddChild(_saveCancel); actions.AddChild(_saveQuit);
        _saveConfirmation = new ConfirmationDialog();
        _saveConfirmation.Confirmed += () => { var action = _saveConfirmedAction; _saveConfirmedAction = null; action?.Invoke(); };
        _saveConfirmation.Canceled += () => { _saveConfirmedAction = null; _modalClosedFrame = Engine.GetProcessFrames(); };
        AddChild(_saveConfirmation);
    }

    private void UpdateSaveButtons()
    {
        _saveButton.Disabled = !CanOpenSaveMenu || _world.Session?.CanSave != true;
        _loadButton.Disabled = !CanOpenSaveMenu;
    }

    private void OpenSaveMenu(bool saving)
    {
        if (!CanOpenSaveMenu || saving && _world.Session?.CanSave != true) return;
        _saving = saving; _shownLifecycle = null;
        DisplaySaveMenu();
    }

    private void PresentLifecycleMenu()
    {
        if (_world.Session?.PendingLifecycle is not { } request || ReferenceEquals(request, _shownLifecycle)) return;
        _shownLifecycle = request; _saving = false;
        DisplaySaveMenu();
    }

    private void DisplaySaveMenu()
    {
        var death = _shownLifecycle?.Kind == EventLifecycleKind.Death;
        _saveCard.AddThemeStyleboxOverride("panel", Box(death ? "38201e" : "15231e", 12));
        _saveTitle.Text = death ? "胜败乃兵家常事" : _saving ? "保存进度" : "载入进度";
        _saveHint.Text = death ? "此番闯荡已经结束。载入存档，再赴江湖。" : _saving
            ? "选择一个位置保存当前进度。" : "选择存档继续游戏。";
        _saveCancel.Visible = _shownLifecycle is null;
        _saveQuit.Visible = _shownLifecycle is not null;
        _saveError.Text = "";
        RefreshSaveSlots();
        _savePanel.Show();
        _heldDirections.Clear(); _world.ReadMovement([], false);
        _saveSlots.FirstOrDefault(button => !button.Disabled)?.GrabFocus();
    }

    private void RefreshSaveSlots()
    {
        try
        {
            foreach (var info in SaveStore().ListSlots())
            {
                var button = _saveSlots[info.Slot - 1];
                var detail = info.Status switch
                {
                    SaveSlotStatus.Empty => "空位置",
                    SaveSlotStatus.Invalid => "存档损坏或格式不兼容",
                    _ => info.DisplayName + "\n" + info.UtcSavedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")
                };
                button.Text = $"进度 {info.Slot}   {detail}";
                button.TooltipText = info.Error ?? detail;
                // Invalid files can be replaced explicitly; they cannot be loaded.
                button.Disabled = !_saving && info.Status != SaveSlotStatus.Valid;
            }
        }
        catch (Exception error)
        {
            _saveError.Text = "无法访问存档目录：" + error.Message;
            foreach (var button in _saveSlots) button.Disabled = true;
        }
    }

    private void SelectSaveSlot(int slot)
    {
        if (!SaveMenuVisible || _saveConfirmation.Visible) return;
        if (!_saving) { TryLoadSlot(slot); return; }
        try
        {
            var info = SaveStore().ListSlots().Single(info => info.Slot == slot);
            if (info.Status != SaveSlotStatus.Empty)
                ConfirmSaveAction("覆盖进度", $"确定覆盖进度 {slot}？", () => TrySaveSlot(slot));
            else TrySaveSlot(slot);
        }
        catch (Exception error) { _saveError.Text = "保存失败：" + error.Message; }
    }

    private void ConfirmSaveAction(string title, string message, Action action)
    {
        _saveConfirmedAction = action;
        _saveConfirmation.Title = title; _saveConfirmation.DialogText = message;
        _saveConfirmation.PopupCentered(new Vector2I(420, 160));
    }

    private void TrySaveSlot(int slot)
    {
        try
        {
            var session = _world.Session ?? throw new InvalidOperationException("No game is running.");
            if (_world.IsMoving || !session.CanSave) throw new InvalidOperationException("请等待行动或剧情结束后再保存。");
            var library = _package?.Library ?? throw new InvalidOperationException("Saving requires the shared resource library.");
            SaveStore().Save(slot, SaveGameDocument.Create(session.CaptureSnapshot(), library, _storyOverlay,
                $"{(session.IsWorld ? "江湖世界" : session.Scene.Name)} · 已行走 {session.Player.Steps} 格"));
            CloseSaveMenu();
        }
        catch (Exception error) { _saveError.Text = "保存失败：" + error.Message; }
    }

    private bool TryLoadSlot(int slot)
    {
        try
        {
            var library = _package?.Library ?? throw new InvalidOperationException("Loading requires the shared resource library.");
            var document = SaveStore().Read(slot);
            var restored = SaveGameLoader.Restore(library, document);
            var old = _world.Session;
            // LoadPackage prepares all textures before committing. Any failure above or
            // during preparation leaves the live game and lifecycle request untouched.
            _world.LoadPackage(restored.View, restored.Session);
            _package = restored.View; _storyOverlay = document.Overlay;
            old?.RetireForReplacement();
            ResetBattleHostAfterLoad();
            _audio.Stop(); _dialogue.Present(null);
            _shownLifecycle = null; CloseSaveMenu();
            _sceneNumber.Value = restored.Session.World.CurrentSceneId;
            BindStory(); _world.QueueRedraw();
            return true;
        }
        catch (Exception error)
        {
            _saveError.Text = "载入失败：存档可能损坏，或使用了不同的资源库。";
            _saveError.TooltipText = error.Message;
            // Do not dismiss a death/load instruction or continue its old event.
            return false;
        }
    }

    private void ResetBattleHostAfterLoad()
    {
        StopBattleAudio(releaseCachedStreams: true);
        _shownBattle = null; _shownBattleActor = null; _shownBattlePhase = null; _battleReturnView = null;
        _shownBattlePresenting = false; _shownBattleConfirmation = null;
        _battleUiError = null; _battleInputMode = BattleInputMode.None;
        _battleControls.Hide();
        foreach (var control in _explorationControls) control.Show();
        _world.BattleMovePreview = false;
    }

    private void CloseSaveMenu()
    {
        if (_shownLifecycle is not null) return;
        _savePanel.Hide(); _heldDirections.Clear();
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        _modalClosedFrame = Engine.GetProcessFrames();
    }
}
