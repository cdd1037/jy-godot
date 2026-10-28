using JinYong.Core.Maps;

namespace JinYong.Core.Events;

public interface IEventWorld
{
    void ChangeEvent(int context, GridPosition trigger, IReadOnlyList<int> arguments);
    void Face(Direction direction);
    void SetPicture(int eventIndex, int picture);
    void AddItem(int id, int count);
    int ItemCount(int id);
    int GetVariable(int id);
    void SetVariable(int id, int value);
    int EventField(int context, IReadOnlyList<int> arguments);
    void SetTile(IReadOnlyList<int> arguments);
    void Teleport(GridPosition position);
    void ValidateCommand(EventCommand command, int context);
    bool ItemExists(int id) => ItemCount(id) > 0;
    bool TestGameCondition(EventCommand command) => throw new InvalidOperationException("Game state service is unavailable.");
    string? ExecuteGameCommand(EventCommand command, int context, GridPosition trigger, int randomValue) =>
        throw new InvalidOperationException("Game state service is unavailable.");
    void SetFade(double opacity) => throw new InvalidOperationException("Fade presentation service is unavailable.");
    void SetCameraAxis(bool horizontal, int coordinate) => throw new InvalidOperationException("Scene camera service is unavailable.");
    void BeginScriptedStep(Direction direction) => throw new InvalidOperationException("Scripted movement service is unavailable.");
    void UpdateScriptedMotion(double progress) => throw new InvalidOperationException("Scripted movement service is unavailable.");
    void EndScriptedWalk() => throw new InvalidOperationException("Scripted movement service is unavailable.");
    void CancelScriptedMotion() { }
    void SetPictureWrapped(int eventIndex, int picture) => SetPicture(eventIndex, unchecked((short)picture));
    void BeginBattle(int battleId, int experienceFlag) => throw new InvalidOperationException("Battle service is unavailable.");
    bool TryGetBattleOutcome(out bool victory) { victory = false; return false; }
    void SetExitMusicOverride(int musicId) => throw new InvalidOperationException("Exit music service is unavailable.");
    void PlayMusic(int musicId) => throw new InvalidOperationException("Music playback service is unavailable.");
    void RestTournamentProtagonist() => throw new InvalidOperationException("Tournament recovery service is unavailable.");
    void BeginShop() => throw new InvalidOperationException("Shop service is unavailable.");
    void EndShop() => throw new InvalidOperationException("Shop service is unavailable.");
    void BeginEnding() => throw new InvalidOperationException("Ending presentation service is unavailable.");
}
