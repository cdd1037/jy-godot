using Godot;
using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Importing;
using JinYong.Importing.Legacy;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class AppRoot
{
    private ScenePackage? CurrentPackage => _viewHost?.Package;
    private StoryDocument? _storyOverlay;
    private bool _openPackage;
    private string _lastSource = "";

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
        _menuController.SetBusy(busy);
        _importButton.Disabled = _openButton.Disabled = busy;
        _previewButton.Disabled = busy || CurrentPackage?.Library is null;
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
            ShowScene(package, directPlay: _directPlay);
            using var config = new ConfigFile();
            config.SetValue("content", "source", _lastSource);
            config.SetValue("content", "package", package.Directory);
            if (!_smoke) config.Save("user://explorer.cfg");
        }
        finally { SetBusy(false); }
    }

    private async Task PreviewSceneAsync()
    {
        if (_busy || CurrentPackage?.Library is not { } library) return;
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

    private void ShowScene(ScenePackage package, bool directPlay = true)
    {
        var old = _world.Session;
        var session = CreateSession(package, directPlay);
        _viewHost.Load(package, session);
        _audio.Stop();
        _dialogue.Present(null);
        old?.RetireForReplacement();
        if (directPlay) _menuController.NotifyGameStarted();
        else _menuController.ShowTitle();
        if (!_menuController.IsOpen) GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
        _sceneNumber.Value = package.Document.LegacySceneId;
        UpdateSceneInfo();
        BindStory();
        _status.Text = $"已进入 {package.Scene.Name}。";
        GD.Print($"SCENE_LOADED id={package.Scene.Id} sprites={package.Document.Sprites.Length} position={package.Scene.Spawn} library={package.Library is not null} overlay={_storyOverlay is not null}");
    }

    private GameSession CreateSession(ScenePackage package, bool startProgram) => new(package.Scene, package.Story,
        skipOpening: !_world.PlayOpening, worldMap: package.Library?.MainMap,
        travelStart: package.Library?.GameData.TravelStart, battles: package.Library?.Battles,
        startInitialProgram: startProgram);

    private void StartNewGame()
    {
        if (_busy || CurrentPackage is not { } package) { _menuController.ResumeFromChild("请先准备游戏资源。"); return; }
        try
        {
            var fresh = package.Library is { } library
                ? library.OpenScene(_storyOverlay?.SceneId ?? library.Manifest.DefaultScene, overlay: _storyOverlay) : package;
            ShowScene(fresh);
        }
        catch (Exception error)
        {
            _menuController.ResumeFromChild("无法开始游戏：" + error.Message);
            GD.PushError(error.ToString());
        }
    }

    private async Task ShowFailureAsync(Exception error)
    {
        _status.Text = "加载失败：" + error.Message;
        _status.TooltipText = error.Message;
        if (!_smoke) _menuController.ReportError("加载失败：" + error.Message);
        GD.PushError(error.ToString());
        if (_smoke) await FinishSmokeAsync(false, error.Message);
    }
}
