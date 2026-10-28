namespace JinYong.Host;

public enum InventoryActionKind { SelectItem, SelectRole, ApplyToRole, UseOnScene, Close, ConfirmPractice, CancelPractice }

/// <summary>Opaque identity returned unchanged by the panel; only the controller inspects it.</summary>
public sealed class InventoryContext
{
    internal InventoryContext() { }
}

public sealed record InventoryIntent(InventoryContext Context, InventoryActionKind Kind, int Value = -1);
public sealed record InventoryItemModel(int Id, string Name, int Count);
public sealed record InventoryRoleModel(int Id, string Name);
public sealed record InventoryConfirmationModel(string Title, string Message);

/// <summary>Detached display values. No panel access to inventory, item rules or mutable roles.</summary>
public sealed class InventoryModel
{
    public InventoryModel(InventoryContext context, IEnumerable<InventoryItemModel> items, IEnumerable<InventoryRoleModel> roles,
        int? selectedItemId, int? selectedRoleId, string itemDetails, string roleDetails, string message,
        string applyText, bool canApply, string applyReason, bool canUseOnScene, InventoryConfirmationModel? confirmation)
    {
        Context = context; Items = Array.AsReadOnly(items.ToArray()); Roles = Array.AsReadOnly(roles.ToArray());
        SelectedItemId = selectedItemId; SelectedRoleId = selectedRoleId; ItemDetails = itemDetails; RoleDetails = roleDetails;
        Message = message; ApplyText = applyText; CanApply = canApply; ApplyReason = applyReason;
        CanUseOnScene = canUseOnScene; Confirmation = confirmation;
    }
    public InventoryContext Context { get; }
    public IReadOnlyList<InventoryItemModel> Items { get; }
    public IReadOnlyList<InventoryRoleModel> Roles { get; }
    public int? SelectedItemId { get; }
    public int? SelectedRoleId { get; }
    public string ItemDetails { get; }
    public string RoleDetails { get; }
    public string Message { get; }
    public string ApplyText { get; }
    public bool CanApply { get; }
    public string ApplyReason { get; }
    public bool CanUseOnScene { get; }
    public InventoryConfirmationModel? Confirmation { get; }
}
