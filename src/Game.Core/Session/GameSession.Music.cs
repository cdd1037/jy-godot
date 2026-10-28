using JinYong.Core.Events;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    private int _exitMusicOverride = -1;
    public int? CurrentMusicId { get; private set; }
    public long MusicRevision { get; private set; }

    void IEventWorld.SetExitMusicOverride(int musicId)
    {
        if (musicId is < short.MinValue or > 23) throw new ArgumentOutOfRangeException(nameof(musicId));
        _exitMusicOverride = musicId;
    }

    // Opcode66 always restarts, including a request for the already playing song.
    void IEventWorld.PlayMusic(int musicId) => RequestMusic(musicId, force: true);

    private void RequestMusic(int musicId, bool force = false)
    {
        if (musicId < 0) return;
        if (musicId > 23) throw new InvalidOperationException("Music ID is outside GAME01..GAME24.");
        if (!force && CurrentMusicId == musicId) return;
        CurrentMusicId = musicId;
        MusicRevision++;
    }

    private void PlayEntranceMusic()
    {
        if (World.HasLegacyMetadata(World.CurrentSceneId)) RequestMusic(World.MetadataWord(-2, 7));
    }

    private void PlayExitMusic()
    {
        if (_exitMusicOverride >= 0) RequestMusic(_exitMusicOverride, force: true);
        else if (World.HasLegacyMetadata(World.CurrentSceneId)) RequestMusic(World.MetadataWord(-2, 6));
        _exitMusicOverride = -1;
    }
}
