using Godot;
using JinYong.Content.Legacy;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Maps;
using JinYong.Core.Events;
using System.Text.Json;

namespace JinYong.Host;

public partial class AppRoot : Control
{
    private SceneView _world = null!;
    private DialoguePanel _dialogue = null!;
    private AudioStreamPlayer _audio = null!;
    private readonly List<AudioStreamWav> _audioStreams = [];
    private readonly List<int> _playedSounds = [];
    private AcceptDialog? _inventoryDialog;
    private ulong? _modalClosedFrame;
    private ulong? _lastConfirmFrame;
    private string? _dialogueCapturePath;
    private bool _dialogueCaptured, _capturingDialogue, _smokeConfirmPressed;
    private readonly HashSet<long> _smokePages = [];
    private Label _sceneName = null!, _coordinates = null!, _status = null!, _details = null!;
    private Label _controlsHint = null!;
    private SpinBox _sceneNumber = null!;
    private Button _importButton = null!, _openButton = null!, _bagButton = null!, _previewButton = null!;
    private CheckButton _smoothMovement = null!;
    private FileDialog _folder = null!;
    private ScenePackage? _package;
    private StoryDocument? _storyOverlay;
    private bool _openPackage;
    private bool _busy;
    private readonly List<Direction> _heldDirections = [];
    private string _lastSource = "";
    private string? _capturePath;
    private string? _reportPath;
    private bool _smoke;
    private bool _eventSmoke;
    private int _eventSmokeStage, _eventSmokeChoiceChecks, _eventSmokeInventoryChecks;
    private int _eventSmokeSilver;
    private bool _inventoryCaptured, _capturingInventory;
    private long _smokeStarted;
    private int _smokeFrames;
    private List<Direction>? _smokeRoute;
    private int _smokeIndex;
    private string? _smokeAction;
    private bool _smokeAcknowledged;
    private bool _smokeQuickTapPending;
    private int _smokeQuickTapChecks;
    private int _smokeMoves;
    private int _smokeBlocks;
    private bool _finishing;
    private string _lastBlock = "";

    public override async void _Ready()
    {
        BuildUi();
        ConfigureInput();
        var args = OS.GetCmdlineUserArgs();
        string? Option(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        _smoke = args.Contains("--smoke-test");
        _eventSmoke = args.Contains("--event-smoke");
        _travelSmoke = args.Contains("--travel-smoke");
        _battleSmoke = args.Contains("--battle-smoke");
        _world.PlayOpening = args.Contains("--play-opening");
        _smoothMovement.ButtonPressed = !args.Contains("--grid-movement");
        _smokeStarted = System.Environment.TickCount64;
        _capturePath = Option("--capture");
        _dialogueCapturePath = Option("--capture-dialogue") ?? (_capturePath is null ? null :
            Path.Combine(Path.GetDirectoryName(_capturePath)!, Path.GetFileNameWithoutExtension(_capturePath) + "-dialogue.png"));
        _reportPath = Option("--report");
        try
        {
            if (Option("--scene") is { } number) _sceneNumber.Value = int.Parse(number);
            if (Option("--story-overlay") is { } overlay) _storyOverlay = ContentJson.Read<StoryDocument>(Path.GetFullPath(overlay));
            if ((Option("--library") ?? Option("--package")) is { } package) await OpenPackageAsync(package);
            else if (Option("--source") is { } source) await ImportAsync(source);
            else if (!_smoke)
            {
                var defaultLibrary = Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "content", "base"));
                if (File.Exists(Path.Combine(defaultLibrary, "manifest.json"))) await OpenPackageAsync(defaultLibrary);
                else
                {
                    using var config = new ConfigFile();
                    if (config.Load("user://explorer.cfg") == Error.Ok)
                    {
                        _lastSource = config.GetValue("content", "source", "").AsString();
                        var previous = config.GetValue("content", "package", "").AsString();
                        if (Directory.Exists(previous)) await OpenPackageAsync(previous);
                    }
                }
            }
            if (_smoke && _world.Session is null) await FinishSmokeAsync(false, "No resource library or compatible scene package was loaded.");
        }
        catch (Exception error) { await ShowFailureAsync(error); }
    }

    private void BuildUi()
    {
        var theme = new Theme { DefaultFont = new SystemFont { FontNames = ["Microsoft YaHei", "Noto Sans CJK SC"] }, DefaultFontSize = 15 };
        theme.SetColor("font_color", "Label", new Color("ddd7c5"));
        theme.SetColor("font_color", "Button", new Color("e6dfcc"));
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetStylebox("normal", "Button", ButtonBox("243b33"));
        theme.SetStylebox("hover", "Button", ButtonBox("355246"));
        theme.SetStylebox("pressed", "Button", ButtonBox("3d5b4a"));
        theme.SetStylebox("disabled", "Button", ButtonBox("192820"));
        Theme = theme;
        var background = new ColorRect { Color = new Color("101916"), MouseFilter = MouseFilterEnum.Ignore };
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(background);
        var margin = new MarginContainer();
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        foreach (var edge in new[] { "left", "right", "top", "bottom" }) margin.AddThemeConstantOverride("margin_" + edge, 24);
        AddChild(margin);
        var page = new VBoxContainer();
        page.AddThemeConstantOverride("separation", 16);
        margin.AddChild(page);
        var header = new HBoxContainer();
        page.AddChild(header);
        var title = new Label { Text = "金庸群侠传", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", new Color("e6d2a4"));
        header.AddChild(title);
        header.AddChild(new Label { Text = "初入江湖  /  场景与剧情", Modulate = new Color("9caf9d") });
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 20);
        page.AddChild(body);
        var mapPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mapPanel.AddThemeStyleboxOverride("panel", Box("101c1a", 12));
        body.AddChild(mapPanel);
        _world = new SceneView { Name = "WorldLayer", CustomMinimumSize = new Vector2(500, 400) };
        mapPanel.AddChild(_world);
        _dialogue = new DialoguePanel { Name = "DialogueOverlay" };
        _dialogue.ContinueRequested += ConfirmDialogue;
        _dialogue.ChoiceRequested += (token, option) => RespondToChoice(token, option);
        _dialogue.ChoiceCancelRequested += token => RespondToChoice(token, -1);
        _world.AddChild(_dialogue);
        _audio = new AudioStreamPlayer { Name = "SceneAudio" };
        AddChild(_audio);
        _world.MovementAttempted += result =>
        {
            _lastBlock = result.Block.ToString();
            if (_smokeAction is not null)
            {
                if (result.Moved) _smokeMoves++; else _smokeBlocks++;
                _smokeAcknowledged = true;
            }
            UpdateSceneInfo();
        };
        var sidePanel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        sidePanel.AddThemeStyleboxOverride("panel", Box("192720", 12));
        body.AddChild(sidePanel);
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        sidePanel.AddChild(scroll);
        var side = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        side.AddThemeConstantOverride("separation", 9);
        scroll.AddChild(side);
        side.AddChild(Caption("当前场景"));
        _sceneName = new Label { Text = "等待导入", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _sceneName.AddThemeFontSizeOverride("font_size", 24);
        side.AddChild(_sceneName);
        _coordinates = new Label { Text = "坐标  —  /  朝向  —", Modulate = new Color("aabcaa") };
        side.AddChild(_coordinates);
        BuildBattleUi(side);
        side.AddChild(new HSeparator());
        side.AddChild(Caption("资源库与场景预览"));
        var numberRow = new HBoxContainer();
        numberRow.AddChild(new Label { Text = "场景编号", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _sceneNumber = new SpinBox { MinValue = 0, MaxValue = 99, Value = 70, CustomMinimumSize = new Vector2(90, 0) };
        numberRow.AddChild(_sceneNumber);
        side.AddChild(numberRow);
        _importButton = new Button { Text = "导入完整原版资源库", CustomMinimumSize = new Vector2(0, 44), FocusMode = FocusModeEnum.None };
        _importButton.Pressed += () => ChooseFolder(false);
        side.AddChild(_importButton);
        _openButton = new Button { Text = "打开已有资源库", CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.None };
        _openButton.Pressed += () => ChooseFolder(true);
        side.AddChild(_openButton);
        _previewButton = new Button { Text = "预览此场景", Disabled = true, CustomMinimumSize = new Vector2(0, 36),
            FocusMode = FocusModeEnum.None, TooltipText = "复用当前资源库，并重启此场景的游戏会话。不是游戏中的场景出口切换。" };
        _previewButton.Pressed += async () => await PreviewSceneAsync();
        side.AddChild(_previewButton);
        var restart = new Button { Text = "重新开始", CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None };
        restart.Pressed += () =>
        {
            if (_package is null || _busy) return;
            try
            {
                var id = _world.Session!.World.CurrentSceneId;
                ShowScene(_package.Library is { } library
                    ? library.OpenScene(id, overlay: _storyOverlay?.SceneId == id ? _storyOverlay : null) : _package);
            }
            catch (Exception error) { _ = ShowFailureAsync(error); }
        };
        side.AddChild(restart);
        _bagButton = new Button { Text = "查看背包", CustomMinimumSize = new Vector2(0, 34), FocusMode = FocusModeEnum.None };
        _bagButton.Pressed += ShowInventory;
        side.AddChild(_bagButton);
        var collision = new CheckButton { Text = "显示碰撞辅助", FocusMode = FocusModeEnum.None };
        collision.Toggled += value => { _world.ShowCollision = value; _world.QueueRedraw(); };
        side.AddChild(collision);
        _smoothMovement = new CheckButton { Text = "平滑移动（F2）", ButtonPressed = true, FocusMode = FocusModeEnum.None };
        _smoothMovement.Toggled += value => { _world.SmoothMovement = value; _world.QueueRedraw(); };
        side.AddChild(_smoothMovement);
        side.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _details = new Label { Text = "原版场景与人物贴图\n格子行走 · 建筑与事件碰撞", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("94a78f") };
        side.AddChild(_details);
        side.AddChild(new HSeparator());
        side.AddChild(new Label { Text = "场景预览会重新开始会话。", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("839580") });
        RememberExplorationUi(side);
        var footer = new HBoxContainer();
        page.AddChild(footer);
        _status = new Label { Text = "选择原版 DOS 游戏目录开始。", SizeFlagsHorizontal = SizeFlags.ExpandFill, TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis };
        footer.AddChild(_status);
        _controlsHint = new Label { Text = "WASD 行走 · E / 空格 交互 · Enter 继续 · 滚轮缩放", Modulate = new Color("8fa38d") };
        footer.AddChild(_controlsHint);
        _folder = new FileDialog { Access = FileDialog.AccessEnum.Filesystem, FileMode = FileDialog.FileModeEnum.OpenDir, UseNativeDialog = true, Title = "选择原版游戏目录" };
        _folder.DirSelected += async path =>
        {
            try { if (_openPackage) await OpenPackageAsync(path); else await ImportAsync(path); }
            catch (Exception error) { await ShowFailureAsync(error); }
        };
        AddChild(_folder);
    }

    private static StyleBoxFlat Box(string color, int radius)
    {
        var box = new StyleBoxFlat { BgColor = new Color(color), ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 16, ContentMarginBottom = 16 };
        box.SetCornerRadiusAll(radius);
        return box;
    }

    private static StyleBoxFlat ButtonBox(string color)
    {
        var box = Box(color, 8);
        box.ContentMarginTop = box.ContentMarginBottom = 8;
        return box;
    }

    private static Label Caption(string text) => new() { Text = text, Modulate = new Color("abbb9d") };

    private void ChooseFolder(bool package)
    {
        if (_busy || _world.Session?.Battle is not null) return;
        _openPackage = package;
        _folder.Title = package ? "选择包含 manifest.json 的资源库（兼容旧场景包）" : "选择原版 DOS 游戏资源目录";
        if (!package && Directory.Exists(_lastSource)) _folder.CurrentDir = _lastSource;
        _folder.PopupCenteredRatio(0.7f);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _importButton.Disabled = _openButton.Disabled = busy;
        _previewButton.Disabled = busy || _package?.Library is null;
    }

    private async Task ImportAsync(string source)
    {
        SetBusy(true);
        try
        {
            _lastSource = Path.GetFullPath(source);
            var destination = ProjectSettings.GlobalizePath($"user://libraries/base-{Guid.NewGuid():N}");
            var options = new LibraryImportOptions(_lastSource, destination);
            var progress = new Progress<string>(message => _status.Text = message);
            var report = await Task.Run(() => new LegacyGameImporter().Import(options, progress));
            await OpenPackageAsync(report.LibraryDirectory);
        }
        finally { SetBusy(false); }
    }

    private async Task OpenPackageAsync(string directory)
    {
        SetBusy(true);
        try
        {
            _status.Text = "正在校验资源库…";
            var scene = (int)_sceneNumber.Value;
            var package = await Task.Run(() => ScenePackageReader.Load(directory, scene, storyOverlay: _storyOverlay));
            ShowScene(package);
            using var config = new ConfigFile();
            config.SetValue("content", "source", _lastSource);
            config.SetValue("content", "package", package.Directory);
            if (!_smoke) config.Save("user://explorer.cfg");
        }
        finally { SetBusy(false); }
    }

    private async Task PreviewSceneAsync()
    {
        if (_busy || _package?.Library is not { } library) return;
        SetBusy(true);
        try
        {
            var scene = (int)_sceneNumber.Value;
            _status.Text = "正在打开场景预览…";
            var package = await Task.Run(() => library.OpenScene(scene, overlay: _storyOverlay));
            ShowScene(package);
            _status.Text = $"正在预览 {package.Scene.Name}；会话已重新开始。";
        }
        catch (Exception error) { await ShowFailureAsync(error); }
        finally { SetBusy(false); }
    }

    private void ShowScene(ScenePackage package)
    {
        _audio.Stop();
        _dialogue.Present(null);
        _world.LoadPackage(package);
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        _package = package;
        _sceneNumber.Value = package.Document.LegacySceneId;
        UpdateSceneInfo();
        BindStory();
        _status.Text = $"已进入 {package.Scene.Name}。";
        GD.Print($"SCENE_LOADED id={package.Scene.Id} sprites={package.Document.Sprites.Length} position={package.Scene.Spawn} library={package.Library is not null} overlay={_storyOverlay is not null}");
    }

    private async Task ShowFailureAsync(Exception error)
    {
        _status.Text = "加载失败：" + error.Message;
        _status.TooltipText = error.Message;
        GD.PushError(error.ToString());
        if (_smoke) await FinishSmokeAsync(false, error.Message);
    }

    private void UpdateSceneInfo()
    {
        if (_world.Session is not { } session) return;
        _sceneName.Text = session.Battle?.Definition.Name ?? (session.IsWorld ? "江湖世界" : session.Scene.Name);
        var player = session.Player;
        var facing = new[] { "东北", "东南", "西北", "西南" }[(int)player.Facing];
        _coordinates.Text = $"坐标  {player.Position.X}, {player.Position.Y}   /   {facing}";
        if (session.Battle is { } battle)
            _coordinates.Text = battle.CurrentActor is { } actor ? $"战场坐标  {actor.Position.X}, {actor.Position.Y}" : "战场 · 64 × 64";
        var location = session.IsWorld ? "世界地图 · 480 × 480" : $"场景 {session.World.CurrentSceneId:D3} · 64 × 64";
        _details.Text = $"已行走 {player.Steps} 格\n{location}\n行囊 {session.Inventory.Count} 种物品";
    }

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
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F1 })
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

    public override void _Process(double delta)
    {
        _world.Advance(delta);
        AdvanceBattleHost(delta);
        CompletePendingTravel();
        BindStory();
        if (_smoke && !_finishing) AdvanceSmoke();
        _heldDirections.RemoveAll(direction => !Input.IsActionPressed(ActionName(direction)));
        foreach (var direction in Enum.GetValues<Direction>())
            if ((Input.IsActionPressed(ActionName(direction)) || Input.IsActionJustPressed(ActionName(direction))) &&
                !_heldDirections.Contains(direction)) _heldDirections.Add(direction);
        var typing = GetViewport().GuiGetFocusOwner() is LineEdit;
        var canWalk = !_finishing && !_busy && !typing && !_folder.Visible && _inventoryDialog?.Visible != true && _modalClosedFrame != Engine.GetProcessFrames();
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
        if (_smokeQuickTapPending)
        {
            _smokeQuickTapPending = false;
            if (!_smokeAcknowledged) _ = FinishSmokeAsync(false, "A direction pressed and released in one frame was lost.");
            else _smokeQuickTapChecks++;
        }
    }

    private void ConfirmDialogue(long token)
    {
        var frame = Engine.GetProcessFrames();
        if (_lastConfirmFrame == frame) return;
        if (_world.Session?.Events?.Confirm(token) == true)
        {
            _lastConfirmFrame = frame;
            _world.QueueRedraw();
        }
    }

    private void BindStory()
    {
        if (_world.Session is not { } session) return;
        UpdateSceneInfo();
        _controlsHint.Text = session.Battle is not null ? "鼠标选择行动与目标 · 滚轮缩放" : "WASD 行走 · E / 空格 交互 · Enter 继续 · 滚轮缩放";
        _bagButton.Disabled = _busy || session.IsInputLocked || _package?.Story is null;
        var pending = session.Events?.Pending;
        _dialogue.Present(pending, pending is null ? null : _world.Portrait(pending.PortraitId),
            pending is null ? null : _package?.Document.PortraitNames.GetValueOrDefault(pending.PortraitId));
        foreach (var soundId in session.Events?.TakeSounds() ?? [])
        {
            if (_package is not { } loadedPackage || !loadedPackage.Document.Sounds.TryGetValue(soundId, out var path))
                throw new InvalidDataException($"Missing event sound {soundId}.");
            var stream = AudioStreamWav.LoadFromFile(loadedPackage.Resolve(path));
            if (stream is null) throw new InvalidDataException($"Invalid audio file {path}.");
            _audioStreams.Add(stream);
            _audio.Stream = stream;
            _audio.Play();
            _playedSounds.Add(soundId);
        }
        if (session.Battle is not null) _status.Text = "选择行动后点击战场格子；战斗结束后按返回继续剧情。";
        else if (session.PendingTravel is not null) _status.Text = "正在前往下一处……";
        else if (session.IsInputLocked && !session.IsEventRunning) _status.Text = "正在进入场景……";
        else if (session.IsEventRunning) _status.Text = pending?.Kind == EventPresentationKind.Choice ? "选择一项继续剧情。" : pending is null ? "……" : "空格 / Enter 继续对话";
        else if (session.InteractionMessage is { } message) _status.Text = message;
        else if (session.IsWorld) _status.Text = "方向键 / WASD 行走，走向入口进入场景。";
        else if (session.Events?.CompletedCount > 0) _status.Text = "面向人物或箱子，按 E / 空格交互。";
    }

    private void RespondToChoice(long token, int option)
    {
        var frame = Engine.GetProcessFrames();
        if (_lastConfirmFrame == frame || _world.Session?.Events is not { } events) return;
        if (option < 0 ? events.CancelChoice(token) : events.Choose(token, option))
        {
            _lastConfirmFrame = frame;
            BindStory(); _world.QueueRedraw();
        }
    }

    private void ShowInventory()
    {
        if (_world.Session is not { } session || session.IsInputLocked || _package?.Story is not { } story || _inventoryDialog is not null) return;
        var dialog = new AcceptDialog
        {
            Title = "行囊"
        };
        _inventoryDialog = dialog;
        void CloseInventory()
        {
            if (_inventoryDialog != dialog) return;
            _modalClosedFrame = Engine.GetProcessFrames();
            _inventoryDialog = null;
            dialog.QueueFree();
        }
        dialog.Confirmed += CloseInventory;
        dialog.Canceled += CloseInventory;
        var column = new VBoxContainer { Name = "InventoryContents" };
        var items = new ItemList { Name = "InventoryItems", CustomMinimumSize = new Vector2(380, 240) };
        var entries = session.Inventory.OrderBy(pair => pair.Key).ToArray();
        foreach (var pair in entries) items.AddItem($"{story.ItemNames.GetValueOrDefault(pair.Key, $"物品 {pair.Key}")}  × {pair.Value}");
        column.AddChild(items);
        var use = new Button { Name = "UseItem", Text = "对面前的物件使用", Disabled = true };
        items.ItemSelected += _ => use.Disabled = session.IsWorld;
        use.Pressed += () =>
        {
            if (_inventoryDialog != dialog) return;
            var selected = items.GetSelectedItems();
            if (selected.Length == 0) return;
            var item = entries[selected[0]].Key;
            CloseInventory();
            session.UseItem(item);
            BindStory(); UpdateSceneInfo(); _world.QueueRedraw();
        };
        column.AddChild(use);
        dialog.AddChild(column);
        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(420, 320));
    }

    private void AdvanceSmoke()
    {
        if (System.Environment.TickCount64 - _smokeStarted > 45000)
        {
            _ = FinishSmokeAsync(false, "Smoke test exceeded 45 seconds.");
            return;
        }
        if (_busy || _world.Session is null) return;
        _smokeFrames++;
        if (_smokeFrames > 3000) { _ = FinishSmokeAsync(false, "Smoke test exceeded 3000 frames."); return; }
        if (_smokeFrames < 20) return;
        if (_smokeConfirmPressed)
        {
            Input.ActionRelease("interact"); _smokeConfirmPressed = false;
            return;
        }
        if (_world.Session.Events?.Error is { } eventError) { _ = FinishSmokeAsync(false, eventError); return; }
        if (_travelSmoke) { AdvanceTravelSmoke(); return; }
        if (_battleSmoke) { AdvanceBattleSmoke(); return; }
        if (_eventSmoke) { AdvanceEventSmoke(); return; }
        if (_world.Session.IsEventRunning)
        {
            if (_world.Session.Events?.Pending is { } pending)
            {
                if (!_dialogueCaptured && _dialogueCapturePath is not null && DisplayServer.GetName() != "headless")
                {
                    if (!_capturingDialogue) _ = CaptureDialogueAsync();
                    return;
                }
                _smokePages.Add(pending.Token);
                Input.ActionPress("interact"); _smokeConfirmPressed = true;
            }
            return;
        }
        if (_smokeRoute is null)
        {
            _smokeRoute = SmokeRoute.Create(_world.Session.Scene);
            if (_smokeRoute.Count < 2) { _ = FinishSmokeAsync(false, "No walking route and collision probe found."); return; }
        }
        if (_smokeAction is not null)
        {
            if (!_smokeAcknowledged) return;
            Input.ActionRelease(_smokeAction);
            _smokeAction = null;
            _smokeIndex++;
            return; // Present a released input frame, including consecutive equal directions.
        }
        if (_world.IsMoving) return;
        if (_smokeIndex >= _smokeRoute.Count)
        {
            var openingPassed = _package?.Story?.InitialProgramId is not { } initial ||
                (_world.Session.Events!.CompletedPrograms.Count(id => id == initial) == 1 &&
                 _world.Session.Scene.Events[_package.Story.InitialEventIndex][4] == -1 &&
                 (_world.PlayOpening ? _smokePages.Count > 0 : _smokePages.Count == 0));
            _ = FinishSmokeAsync(openingPassed && _smokeMoves == _smokeRoute.Count - 1 && _smokeBlocks == 1 && _world.DrawnSprites > 0,
                _world.PlayOpening ? "Opening completed through input confirmations; then walked and tested collision."
                    : "Started in free roam with opening state applied; then walked and tested collision.");
            return;
        }
        _smokeAcknowledged = false;
        _smokeAction = ActionName(_smokeRoute[_smokeIndex]);
        Input.ActionPress(_smokeAction);
        if (_smokeIndex == 0)
        {
            Input.ActionRelease(_smokeAction);
            _smokeQuickTapPending = true;
        }
    }

    private async Task CaptureDialogueAsync()
    {
        _capturingDialogue = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = Path.GetFullPath(_dialogueCapturePath!);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { await FinishSmokeAsync(false, "Dialogue screenshot failed."); return; }
        _dialogueCaptured = true;
        _capturingDialogue = false;
    }

    private void AdvanceEventSmoke()
    {
        var session = _world.Session!;
        var events = session.Events!;
        if (_eventSmokeStage == 0)
        {
            if (session.IsEventRunning)
            {
                if (events.Pending is not null) { Input.ActionPress("interact"); _smokeConfirmPressed = true; }
                return;
            }
            _eventSmokeSilver = session.Inventory.GetValueOrDefault(174);
            Input.ActionPress("interact"); _smokeConfirmPressed = true;
            _eventSmokeStage = 1;
            return;
        }
        if (events.Pending is { } pending)
        {
            if (pending.Kind == EventPresentationKind.Choice)
            {
                if (!_dialogueCaptured && _dialogueCapturePath is not null && DisplayServer.GetName() != "headless")
                {
                    if (!_capturingDialogue) _ = CaptureDialogueAsync();
                    return;
                }
                if (_eventSmokeChoiceChecks++ != 0) { _ = FinishSmokeAsync(false, "Quest returned to its initial choice."); return; }
            }
            Input.ActionPress("interact"); _smokeConfirmPressed = true;
            return;
        }
        if (session.IsEventRunning) return;
        if (_eventSmokeStage == 1)
        {
            if (session.Variables.GetValueOrDefault("example:quest/progress") != 1 || session.Inventory.GetValueOrDefault(9000) != 1)
            { _ = FinishSmokeAsync(false, "Quest acceptance did not grant its token."); return; }
            _bagButton.EmitSignal(BaseButton.SignalName.Pressed);
            _eventSmokeStage = 2;
            return;
        }
        if (_eventSmokeStage == 2)
        {
            if (_inventoryDialog is not { Visible: true } dialog) { _ = FinishSmokeAsync(false, "Inventory did not open."); return; }
            if (_capturePath is not null && !_inventoryCaptured && DisplayServer.GetName() != "headless")
            {
                if (!_capturingInventory) _ = CaptureInventoryAsync();
                return;
            }
            var items = dialog.GetNode<ItemList>("InventoryContents/InventoryItems");
            var use = dialog.GetNode<Button>("InventoryContents/UseItem");
            var index = Array.FindIndex(session.Inventory.OrderBy(pair => pair.Key).ToArray(), pair => pair.Key == 9000);
            items.Select(index);
            items.EmitSignal(ItemList.SignalName.ItemSelected, (long)index);
            if (use.Disabled) { _ = FinishSmokeAsync(false, "Inventory item could not be selected for use."); return; }
            use.EmitSignal(BaseButton.SignalName.Pressed);
            _eventSmokeInventoryChecks++;
            _eventSmokeStage = 3;
            return;
        }
        if (_eventSmokeStage == 3)
        {
            if (session.Variables.GetValueOrDefault("example:quest/progress") != 2 || session.Inventory.GetValueOrDefault(9000) != 0 ||
                session.Inventory.GetValueOrDefault(174) != _eventSmokeSilver + 100)
            { _ = FinishSmokeAsync(false, "Quest delivery state or reward is incorrect."); return; }
            Input.ActionPress("interact"); _smokeConfirmPressed = true;
            _eventSmokeStage = 4;
            return;
        }
        _ = FinishSmokeAsync(_eventSmokeChoiceChecks == 1 && _eventSmokeInventoryChecks == 1 &&
            session.Inventory.GetValueOrDefault(174) == _eventSmokeSilver + 100 && session.Inventory.GetValueOrDefault(9000) == 0,
            "Accepted a quest through choice input, used its token through inventory, received one reward, and tested repeat interaction.");
    }

    private async Task CaptureInventoryAsync()
    {
        _capturingInventory = true;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var path = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(_capturePath!))!, Path.GetFileNameWithoutExtension(_capturePath) + "-inventory.png");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var image = GetViewport().GetTexture().GetImage();
        if (image.SavePng(path) != Error.Ok) { await FinishSmokeAsync(false, "Inventory screenshot failed."); return; }
        _inventoryCaptured = true;
        _capturingInventory = false;
    }

    private async Task FinishSmokeAsync(bool success, string message)
    {
        if (_finishing) return;
        _finishing = true;
        if (_smokeAction is not null) Input.ActionRelease(_smokeAction);
        if (_capturePath is not null && DisplayServer.GetName() != "headless")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_capturePath))!);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(_capturePath) != Error.Ok) { success = false; message += " Screenshot failed."; }
        }
        var report = new
        {
            success, message, scene = _package?.Scene.Id, position = _world.Session?.Player.Position,
            steps = _smokeMoves, blockedMoves = _smokeBlocks, lastBlock = _lastBlock,
            drawnSprites = _world.DrawnSprites, frames = _smokeFrames, package = _package?.Directory
            , dialoguePages = _smokePages.Count, completedPrograms = _world.Session?.Events?.CompletedPrograms,
            playedSounds = _playedSounds, openingTrigger = _world.Session?.Scene.Events[0][4]
            , quickTapChecks = _smokeQuickTapChecks, smoothMovement = _world.SmoothMovement, playOpening = _world.PlayOpening
            , eventChoiceChecks = _eventSmokeChoiceChecks, inventoryUseChecks = _eventSmokeInventoryChecks,
            travelChecks = _travelChecks,
            battleChecks = _battleChecks, battleMoves = _battleMoves, battleAttacks = _battleAttacks,
            battleOutcome = _world.Session?.LastBattleOutcome,
            variables = _world.Session?.Variables, inventory = _world.Session?.Inventory,
            eventTrace = _world.Session?.Events?.Trace
        };
        if (_reportPath is not null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_reportPath))!);
            File.WriteAllText(_reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        GD.Print((success ? "SMOKE_PASS " : "SMOKE_FAIL ") + JsonSerializer.Serialize(report));
        GetTree().Quit(success ? 0 : 1);
    }

    public override void _ExitTree()
    {
        _audio.Stop();
        _audio.Stream = null;
        foreach (var stream in _audioStreams) stream.Dispose();
    }
}
