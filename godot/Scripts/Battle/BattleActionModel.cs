namespace JinYong.Host;

public enum BattleActionKind
{
    ConfirmParty, SelectMove, SelectMagic, SelectAttack, SelectPoison, SelectDetox, SelectMedicine,
    SelectItem, UseItem, Rest, Wait, ConfirmPresentation, ShowResults
}

/// <summary>Opaque identity for one rendered set of actions; contains no game state.</summary>
public sealed class BattleActionToken
{
    internal BattleActionToken() { }
}
public sealed record BattleActionIntent(BattleActionToken Token, BattleActionKind Kind, int Value = -1,
    IReadOnlyList<int>? SelectedRoles = null);
public sealed record BattlePartyOption(int Id, string Name, bool Mandatory);
public sealed record BattleMagicOption(int Slot, string Name);
public sealed record BattleItemOption(int Id, string Name);

/// <summary>Read-only display state. Text and availability are supplied by the controller.</summary>
public sealed record BattleActionModel
{
    public BattleActionModel(BattleActionToken token, string status, bool isSelecting, bool finished, bool present,
        bool waitForConfirmation, bool isFriendlyTurn, IReadOnlyList<BattlePartyOption> partyOptions,
        IReadOnlyList<BattleMagicOption> magicOptions, IReadOnlyList<BattleItemOption> itemOptions,
        bool canMove, bool canPoison, bool canDetox, bool canMedicine, string targetHint,
        int selectedMagicSlot = -1, int selectedItemId = -1)
    {
        ArgumentNullException.ThrowIfNull(token);
        Token = token; Status = status; IsSelecting = isSelecting; Finished = finished; Present = present;
        WaitForConfirmation = waitForConfirmation; IsFriendlyTurn = isFriendlyTurn;
        PartyOptions = Array.AsReadOnly(partyOptions.ToArray()); MagicOptions = Array.AsReadOnly(magicOptions.ToArray());
        ItemOptions = Array.AsReadOnly(itemOptions.ToArray());
        CanMove = canMove; CanPoison = canPoison; CanDetox = canDetox; CanMedicine = canMedicine;
        TargetHint = targetHint; SelectedMagicSlot = selectedMagicSlot; SelectedItemId = selectedItemId;
    }
    public BattleActionToken Token { get; }
    public string Status { get; }
    public bool IsSelecting { get; }
    public bool Finished { get; }
    public bool Present { get; }
    public bool WaitForConfirmation { get; }
    public bool IsFriendlyTurn { get; }
    public IReadOnlyList<BattlePartyOption> PartyOptions { get; }
    public IReadOnlyList<BattleMagicOption> MagicOptions { get; }
    public IReadOnlyList<BattleItemOption> ItemOptions { get; }
    public bool CanMove { get; }
    public bool CanPoison { get; }
    public bool CanDetox { get; }
    public bool CanMedicine { get; }
    public string TargetHint { get; }
    public int SelectedMagicSlot { get; }
    public int SelectedItemId { get; }
}
