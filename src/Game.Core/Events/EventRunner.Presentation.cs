namespace JinYong.Core.Events;

public sealed partial class EventRunner
{
    private readonly Queue<int> _sounds = [];
    private DialogueDefinition? _dialogue;
    private int _page, _portrait, _style, _cancelTarget = -1;
    public EventPresentation? Pending { get; private set; }

    public bool Confirm(long token)
    {
        if (Pending is not { } pending || pending.Token != token) return false;
        if (pending.Kind == EventPresentationKind.Choice) return false;
        if (pending.Kind == EventPresentationKind.Error) { Pending = null; return true; }
        if (_dialogue is not null && ++_page < _dialogue.Pages.Count) { ShowPage(); return true; }
        _dialogue = null;
        Pending = null;
        Pump();
        return true;
    }

    public bool Choose(long token, int option)
    {
        if (Pending is not { Kind: EventPresentationKind.Choice, Choices: { } choices } pending || pending.Token != token ||
            option < 0 || option >= choices.Count) return false;
        _cursor = _program!.IndexOf(choices[option].Target);
        Pending = null;
        Pump();
        return true;
    }

    public bool CancelChoice(long token)
    {
        if (Pending is not { Kind: EventPresentationKind.Choice, CanCancel: true } pending || pending.Token != token) return false;
        _cursor = _program!.IndexOf(_cancelTarget);
        Pending = null;
        Pump();
        return true;
    }

    public int[] TakeSounds()
    {
        var result = _sounds.ToArray();
        _sounds.Clear();
        return result;
    }

    private void ShowPage()
    {
        var dialogue = _dialogue!;
        Pending = new(++_token, EventPresentationKind.Dialogue, dialogue.Id, _portrait, _style,
            dialogue.Pages[_page], _page + 1, dialogue.Pages.Count);
    }

    private void StartDialogue(int id, int portrait, int style)
    {
        _dialogue = _catalog.Dialogues.TryGetValue(id, out var dialogue) ? dialogue
            : throw new InvalidOperationException($"Missing dialogue {id}.");
        _page = 0; _portrait = portrait; _style = style;
        ShowPage();
    }
}
