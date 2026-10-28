namespace JinYong.Core.Events;

public enum EventPresentationKind { Dialogue, Item, Error, Choice, Notice }
public sealed record EventPresentation(long Token, EventPresentationKind Kind, int DialogueId,
    int PortraitId, int Style, string Text, int Page, int PageCount, IReadOnlyList<EventChoice>? Choices = null, bool CanCancel = false);
public sealed record EventTrace(int Program, int Command, EventOperation Operation);
public sealed record EventBattleRequest(long Token, int BattleId, int ExperienceFlag);
public enum EventLifecycleKind { Death, LoadMenu }
/// <summary>
/// A terminal handoff to the host's three-slot load/quit menu, not a dialogue or
/// a resumable instruction. DOS opcode15 ignores SourceArgument (normally83).
/// </summary>
public sealed record EventLifecycleRequest(long Token, EventLifecycleKind Kind, int SourceArgument = -1);
