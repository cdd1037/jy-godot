using Godot;
using JinYong.Content.Library;
using JinYong.Core.Battles;

namespace JinYong.Host;

public partial class AppRoot
{
    private readonly Dictionary<BattleSoundBank, AudioStreamPlayer> _battleSoundPlayers = [];
    private readonly Dictionary<BattleSoundBank, (int Id, AudioStreamWav Stream)> _loadedBattleSounds = [];
    private readonly Dictionary<string, AudioStreamWav> _battleAudioCache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedBattleAudioErrors = [];
    private readonly List<string> _playedBattleSounds = [];
    private GameLibrary? _battleAudioLibrary;

    private void BuildBattleAudio()
    {
        // Independent banks allow an attack's voice and its effect to overlap.
        foreach (var bank in Enum.GetValues<BattleSoundBank>())
        {
            var player = new AudioStreamPlayer { Name = $"Battle{bank}Audio" };
            AddChild(player); _battleSoundPlayers.Add(bank, player);
        }
    }

    private void PlayBattlePresentationSounds(BattleSession battle)
    {
        if (_package?.Library is not { } library) return;
        if (!ReferenceEquals(_battleAudioLibrary, library))
        {
            StopBattleAudio(releaseCachedStreams: true);
            _battleAudioLibrary = library;
        }
        foreach (var sound in battle.TakePresentationSounds())
        {
            try
            {
                if (sound.Action == BattleSoundAction.Load)
                {
                    // A failed replacement must never replay the previous track.
                    _loadedBattleSounds.Remove(sound.Bank);
                    var index = sound.Bank == BattleSoundBank.Attack ? library.Audio.Attacks : library.Audio.Effects;
                    if (!index.TryGetValue(sound.Id, out var relative))
                        throw new InvalidDataException($"Missing battle sound {sound.Bank}:{sound.Id}.");
                    var path = library.Resolve(relative);
                    if (!_battleAudioCache.TryGetValue(path, out var stream))
                    {
                        stream = AudioStreamWav.LoadFromFile(path) ??
                            throw new InvalidDataException($"Invalid battle sound {sound.Bank}:{sound.Id}: {relative}.");
                        _battleAudioCache.Add(path, stream);
                    }
                    _loadedBattleSounds[sound.Bank] = (sound.Id, stream);
                }
                else
                {
                    if (!_loadedBattleSounds.TryGetValue(sound.Bank, out var loaded) || loaded.Id != sound.Id)
                        throw new InvalidDataException($"Battle sound was not loaded: {sound.Bank}:{sound.Id}.");
                    var player = _battleSoundPlayers[sound.Bank];
                    player.Stream = loaded.Stream; player.Play();
                    _playedBattleSounds.Add($"{sound.Bank}:{sound.Id}");
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                _battleUiError = "战斗音效加载失败：" + error.Message;
                if (_reportedBattleAudioErrors.Add(error.Message)) GD.PushError(error.ToString());
            }
        }
    }

    private void StopBattleAudio(bool releaseCachedStreams = false)
    {
        foreach (var player in _battleSoundPlayers.Values) { player.Stop(); player.Stream = null; }
        _loadedBattleSounds.Clear();
        if (!releaseCachedStreams) return;
        foreach (var stream in _battleAudioCache.Values) stream.Dispose();
        _battleAudioCache.Clear(); _reportedBattleAudioErrors.Clear(); _battleAudioLibrary = null;
    }
}
