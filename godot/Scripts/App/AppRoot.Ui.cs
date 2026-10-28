using Godot;

namespace JinYong.Host;

public partial class AppRoot
{
    private Label _sceneName = null!, _coordinates = null!, _status = null!, _details = null!;
    private Label _controlsHint = null!;
    private SpinBox _sceneNumber = null!;
    private Button _importButton = null!, _openButton = null!, _bagButton = null!, _previewButton = null!;
    private CheckButton _smoothMovement = null!;
    private FileDialog _folder = null!;
    private VBoxContainer _explorationPanel = null!;
    private PanelContainer _sidePanel = null!;
    private Button _menuButton = null!;

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
        _menuButton = new Button { Name = "OpenGameMenu", Text = "菜单（Esc）", FocusMode = FocusModeEnum.None };
        _menuButton.Pressed += () => _menuController.OpenGameMenu();
        header.AddChild(_menuButton);
        var body = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 20);
        page.AddChild(body);
        var mapPanel = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        mapPanel.AddThemeStyleboxOverride("panel", Box("101c1a", 12));
        body.AddChild(mapPanel);
        _world = new SceneView { Name = "WorldLayer", CustomMinimumSize = new Vector2(500, 400) };
        _viewHost = new(_world);
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
            RecordSmokeMovement(result);
            UpdateSceneInfo();
        };
        var sidePanel = new PanelContainer { CustomMinimumSize = new Vector2(280, 0) };
        _sidePanel = sidePanel;
        sidePanel.Visible = false;
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
        _explorationPanel = new VBoxContainer { Name = "ExplorationControls", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _explorationPanel.AddThemeConstantOverride("separation", 9);
        ComposeGameplayComponents(side);
        side.AddChild(_explorationPanel);
        _explorationPanel.AddChild(new HSeparator());
        _explorationPanel.AddChild(Caption("资源库与场景预览"));
        var numberRow = new HBoxContainer();
        numberRow.AddChild(new Label { Text = "场景编号", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _sceneNumber = new SpinBox { MinValue = 0, MaxValue = 99, Value = 70, CustomMinimumSize = new Vector2(90, 0) };
        numberRow.AddChild(_sceneNumber);
        _explorationPanel.AddChild(numberRow);
        _importButton = new Button { Text = "导入完整原版资源库", CustomMinimumSize = new Vector2(0, 44), FocusMode = FocusModeEnum.None };
        _importButton.Pressed += () => ChooseFolder(false);
        _explorationPanel.AddChild(_importButton);
        _openButton = new Button { Text = "打开已有资源库", CustomMinimumSize = new Vector2(0, 40), FocusMode = FocusModeEnum.None };
        _openButton.Pressed += () => ChooseFolder(true);
        _explorationPanel.AddChild(_openButton);
        _previewButton = new Button { Text = "预览此场景", Disabled = true, CustomMinimumSize = new Vector2(0, 36),
            FocusMode = FocusModeEnum.None, TooltipText = "复用当前资源库，并重启此场景的游戏会话。不是游戏中的场景出口切换。" };
        _previewButton.Pressed += async () => await PreviewSceneAsync();
        _explorationPanel.AddChild(_previewButton);
        var restart = new Button { Text = "重新开始", CustomMinimumSize = new Vector2(0, 38), FocusMode = FocusModeEnum.None };
        restart.Pressed += () =>
        {
            if (CurrentPackage is not { } package || _busy) return;
            try
            {
                var id = _world.Session!.World.CurrentSceneId;
                ShowScene(package.Library is { } library
                    ? library.OpenScene(id, overlay: _storyOverlay?.SceneId == id ? _storyOverlay : null) : package);
            }
            catch (Exception error) { _ = ShowFailureAsync(error); }
        };
        _explorationPanel.AddChild(restart);
        _bagButton = new Button { Text = "查看背包", CustomMinimumSize = new Vector2(0, 34), FocusMode = FocusModeEnum.None };
        _bagButton.Pressed += ShowInventory;
        _explorationPanel.AddChild(_bagButton);
        BuildSaveButtons(_explorationPanel);
        var collision = new CheckButton { Text = "显示碰撞辅助", FocusMode = FocusModeEnum.None };
        collision.Toggled += value => { _world.ShowCollision = value; _world.QueueRedraw(); };
        _explorationPanel.AddChild(collision);
        _smoothMovement = new CheckButton { Text = "平滑移动（F2）", ButtonPressed = true, FocusMode = FocusModeEnum.None };
        _smoothMovement.Toggled += value => _menuController.SetPreferences(_menuController.Preferences with { SmoothMovement = value });
        _explorationPanel.AddChild(_smoothMovement);
        _explorationPanel.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });
        _details = new Label { Text = "原版场景与人物贴图\n格子行走 · 建筑与事件碰撞", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("94a78f") };
        _explorationPanel.AddChild(_details);
        _explorationPanel.AddChild(new HSeparator());
        _explorationPanel.AddChild(new Label { Text = "场景预览会重新开始会话。", AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color("839580") });
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
            finally { _menuController.ResumeFromChild(); }
        };
        _folder.Canceled += () => _menuController.ResumeFromChild();
        AddChild(_folder);
        BuildSaveMenu();
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

    private void UpdateSceneInfo()
    {
        if (_world.Session is not { } session) return;
        _sceneName.Text = session.Battle?.Definition.Name ?? (session.IsWorld ? "江湖世界" : session.Scene.Name);
        var player = session.Player;
        var facing = new[] { "东北", "东南", "西北", "西南" }[(int)player.Facing];
        _coordinates.Text = $"坐标  {player.Position.X}, {player.Position.Y}   /   {facing}";
        if (session.Battle is { } battle)
        {
            var actor = battle.CurrentActor;
            var position = battle.Presentation?.Combatants.FirstOrDefault(person => person.Slot == actor?.Slot)?.Position ?? actor?.Position;
            _coordinates.Text = position is { } cell ? $"战场坐标  {cell.X}, {cell.Y}" : "战场 · 64 × 64";
        }
        var location = session.IsWorld ? "世界地图 · 480 × 480" : $"场景 {session.World.CurrentSceneId:D3} · 64 × 64";
        _details.Text = $"已行走 {player.Steps} 格\n{location}\n行囊 {session.Inventory.Count} 种物品";
    }
}
