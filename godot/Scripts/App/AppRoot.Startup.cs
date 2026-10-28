using Godot;
using JinYong.Content.Packages;

namespace JinYong.Host;

public partial class AppRoot
{
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
        _itemActionsSmoke = args.Contains("--item-actions-smoke");
        _saveSmoke = args.Contains("--save-smoke");
        _savesDirectory = Option("--saves-directory") ?? ProjectSettings.GlobalizePath("user://saves");
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
            if (_saveSmoke) PrepareSaveSmokeOverlay();
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
}
