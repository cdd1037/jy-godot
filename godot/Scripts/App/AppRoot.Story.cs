using Godot;
using JinYong.Core.Events;

namespace JinYong.Host;

public partial class AppRoot
{
    private DialoguePanel _dialogue = null!;
    private AudioStreamPlayer _audio = null!;
    private readonly List<AudioStreamWav> _audioStreams = [];
    private readonly List<int> _playedSounds = [];
    private ulong? _lastConfirmFrame;

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
        _controlsHint.Text = BattleResultsVisible ? "Enter / 空格继续剧情" : session.Battle is not null
            ? "鼠标选择行动与目标 · Esc 取消 · 滚轮缩放" : "WASD 行走 · E / 空格 交互 · Enter 继续 · 滚轮缩放";
        _bagButton.Disabled = _busy || !session.CanSave || _world.IsMoving || _package?.Story is null;
        UpdateSaveButtons();
        if (session.Battle is { } battle) PlayBattlePresentationSounds(battle);
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
        if (BattleResultsVisible) _status.Text = "查看战果后，按 Enter / 空格或点击继续剧情。";
        else if (session.PendingLifecycle is not null) _status.Text = "请载入存档或退出游戏。";
        else if (session.Battle?.Presentation is { WaitingForConfirmation: true }) _status.Text = "按任意键或点击继续，关闭药效面板。";
        else if (session.Battle?.IsPresenting == true) _status.Text = "正在行动……";
        else if (session.Battle is not null) _status.Text = "选择行动后点击战场格子；战斗结束后按返回继续剧情。";
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
}
