using Godot;
using JinYong.Content.Library;
using JinYong.Core.Battles;

namespace JinYong.Host;

/// <summary>Owns the two native sound banks and their active library's WAV cache.</summary>
public partial class BattleAudioPlayer : Node
{
    private readonly Dictionary<BattleSoundBank, AudioStreamPlayer> _players = [];
    private readonly Dictionary<BattleSoundBank, (int Id, AudioStreamWav Stream)> _loaded = [];
    private readonly Dictionary<string, AudioStreamWav> _cache = new(StringComparer.Ordinal);
    private readonly HashSet<string> _reportedErrors = [];
    private readonly List<string> _played = [];
    private readonly IReadOnlyList<string> _playedView;
    private GameLibrary? _library;

    public BattleAudioPlayer()
    {
        _playedView = _played.AsReadOnly();
        // Independent banks allow an attack's voice and its effect to overlap.
        foreach (var bank in Enum.GetValues<BattleSoundBank>())
        {
            var player = new AudioStreamPlayer { Name = $"Battle{bank}Audio" };
            AddChild(player);
            _players.Add(bank, player);
        }
    }

    public IReadOnlyList<string> PlayedSounds => _playedView;
    public event Action<string>? PlaybackFailed;

    /// <summary>Consumes the supplied cues in order; the caller owns the Core queue.</summary>
    public void Play(GameLibrary library, IReadOnlyList<BattlePresentationSound> sounds)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(sounds);
        if (!ReferenceEquals(_library, library))
        {
            Stop(releaseCachedStreams: true);
            _library = library;
        }
        foreach (var sound in sounds)
        {
            try
            {
                if (!Enum.IsDefined(sound.Bank) || !Enum.IsDefined(sound.Action))
                    throw new ArgumentException("Unknown battle sound bank or action.");
                if (sound.Action == BattleSoundAction.Load)
                {
                    // A failed replacement must never replay the previous track.
                    _loaded.Remove(sound.Bank);
                    var index = sound.Bank == BattleSoundBank.Attack ? library.Audio.Attacks : library.Audio.Effects;
                    if (!index.TryGetValue(sound.Id, out var relative))
                        throw new InvalidDataException($"Missing battle sound {sound.Bank}:{sound.Id}.");
                    var path = library.Resolve(relative);
                    if (!_cache.TryGetValue(path, out var stream))
                    {
                        stream = AudioStreamWav.LoadFromFile(path) ??
                            throw new InvalidDataException($"Invalid battle sound {sound.Bank}:{sound.Id}: {relative}.");
                        _cache.Add(path, stream);
                    }
                    _loaded[sound.Bank] = (sound.Id, stream);
                }
                else
                {
                    if (!_loaded.TryGetValue(sound.Bank, out var loaded) || loaded.Id != sound.Id)
                        throw new InvalidDataException($"Battle sound was not loaded: {sound.Bank}:{sound.Id}.");
                    var player = _players[sound.Bank];
                    player.Stream = loaded.Stream; player.Play();
                    _played.Add($"{sound.Bank}:{sound.Id}");
                }
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException)
            {
                if (_reportedErrors.Add(error.Message)) GD.PushError(error.ToString());
                PlaybackFailed?.Invoke("战斗音效加载失败：" + error.Message);
            }
        }
    }

    public void Stop(bool releaseCachedStreams = false)
    {
        foreach (var player in _players.Values) { player.Stop(); player.Stream = null; }
        _loaded.Clear();
        if (!releaseCachedStreams) return;
        foreach (var stream in _cache.Values) stream.Dispose();
        _cache.Clear(); _reportedErrors.Clear(); _library = null;
    }

    public override void _ExitTree() => Stop(releaseCachedStreams: true);
}
