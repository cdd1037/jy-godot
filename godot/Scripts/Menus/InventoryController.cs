using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Items;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Owns inventory selection/confirmation and delegates every mutation to GameSession.</summary>
public sealed class InventoryController : IDisposable
{
    private readonly GameViewHost _host;
    private readonly InventoryPanel _panel;
    private GameSession? _session;
    private InventoryModel? _model;
    private sealed record InputOwner(InventoryContext Context, GameSession Session, long Generation, int? ItemId, int? RoleId);
    private InputOwner? _owner;
    private int? _itemId, _roleId;
    private long _generation, _shownRevision;
    private ulong _blockedFrame;
    private bool _disposed;
    private string _message = "请选择物品和队员。";
    private PracticeApproval? _approval;
    private sealed record PracticeApproval(int ItemId, int RoleId, bool Transfer, bool Castration,
        ExplorationItemUseStatus Requested, string Message);

    public InventoryController(GameViewHost host, InventoryPanel panel)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        panel.IntentRequested += HandleIntent;
        panel.KeyInput += PanelKeyInput;
        host.SessionChanged += Close;
    }

    public bool IsOpen => _session is not null;
    internal InventoryPanel Panel => _panel;
    public event Action? InputAcknowledged;
    public event Action? Closed;

    public bool Open()
    {
        if (_disposed || IsOpen || _host.Session is not { CanSave: true } session || _host.View.IsMoving || _host.Package?.Story is null)
            return false;
        _session = session; _approval = null; _itemId = null;
        _roleId = session.Party?.Members.Where(id => id >= 0).Select(id => (int?)id).FirstOrDefault();
        _message = "请选择物品和队员。";
        _blockedFrame = Engine.GetProcessFrames();
        Render();
        return true;
    }

    public void Close()
    {
        if (!IsOpen) return;
        _session = null; _model = null; _owner = null; _approval = null; _itemId = _roleId = null; _generation++;
        _panel.Close();
        InputAcknowledged?.Invoke();
        Closed?.Invoke();
    }

    public void Advance()
    {
        if (_session is not { } session) return;
        if (!ReferenceEquals(_host.Session, session) || !session.CanSave || _host.View.IsMoving) { Close(); return; }
        if (_shownRevision != session.PresentationRevision) Render();
    }

    public bool HandleKeyInput(InputEvent input)
    {
        if (!IsOpen || input is not InputEventKey { Pressed: true, Echo: false } key) return false;
        if (_blockedFrame == Engine.GetProcessFrames()) return true;
        if (_model is not { } model) return true;
        if (model.Confirmation is not null)
        {
            if (key.Keycode == Key.Escape) HandleIntent(new(model.Context, InventoryActionKind.CancelPractice));
            else if (key.Keycode is Key.Enter or Key.KpEnter or Key.Space) HandleIntent(new(model.Context, InventoryActionKind.ConfirmPractice));
            return true;
        }
        if (key.Keycode == Key.Escape) { Close(); return true; }
        if (key.Keycode is Key.Up or Key.Down or Key.W or Key.S)
        {
            var index = model.Items.ToList().FindIndex(item => item.Id == _itemId);
            var step = key.Keycode is Key.Up or Key.W ? -1 : 1;
            if (model.Items.Count > 0)
            {
                var next = index < 0 ? (step > 0 ? 0 : model.Items.Count - 1) :
                    (index + step + model.Items.Count) % model.Items.Count;
                HandleIntent(new(model.Context, InventoryActionKind.SelectItem, model.Items[next].Id));
            }
            return true;
        }
        if (key.Keycode is Key.Left or Key.Right or Key.A or Key.D)
        {
            var index = model.Roles.ToList().FindIndex(role => role.Id == _roleId);
            var step = key.Keycode is Key.Left or Key.A ? -1 : 1;
            if (model.Roles.Count > 0)
            {
                var next = index < 0 ? 0 : (index + step + model.Roles.Count) % model.Roles.Count;
                HandleIntent(new(model.Context, InventoryActionKind.SelectRole, model.Roles[next].Id));
            }
            return true;
        }
        if (key.Keycode is Key.Enter or Key.KpEnter or Key.Space)
            HandleIntent(new(model.Context, InventoryActionKind.ApplyToRole));
        else if (key.Keycode == Key.E)
            HandleIntent(new(model.Context, InventoryActionKind.UseOnScene));
        return true;
    }

    private void PanelKeyInput(InputEvent input) => HandleKeyInput(input);

    private bool IsCurrent(InventoryContext context) => !_disposed && _session is { CanSave: true } session &&
        _owner is { } owner && ReferenceEquals(context, owner.Context) &&
        ReferenceEquals(_host.Session, session) && ReferenceEquals(owner.Session, session) && !_host.View.IsMoving &&
        owner.Generation == _generation && owner.ItemId == _itemId && owner.RoleId == _roleId;

    private void HandleIntent(InventoryIntent intent)
    {
        if (!IsCurrent(intent.Context) || _blockedFrame == Engine.GetProcessFrames()) return;
        if (_approval is not null && intent.Kind is not (InventoryActionKind.ConfirmPractice or InventoryActionKind.CancelPractice)) return;
        switch (intent.Kind)
        {
            case InventoryActionKind.SelectItem:
                if (_session!.Inventory.GetValueOrDefault(intent.Value) <= 0) return;
                _itemId = intent.Value; _message = ""; Render(); break;
            case InventoryActionKind.SelectRole:
                if (_session!.Party?.Contains(intent.Value) != true) return;
                _roleId = intent.Value; _message = ""; Render(); break;
            case InventoryActionKind.ApplyToRole:
                if (_model?.CanApply == true) ApplyToRole(false, false);
                break;
            case InventoryActionKind.ConfirmPractice:
                if (_approval is not { } approval || approval.ItemId != _itemId || approval.RoleId != _roleId) return;
                ApplyToRole(approval.Transfer || approval.Requested == ExplorationItemUseStatus.ConfirmTransfer,
                    approval.Castration || approval.Requested == ExplorationItemUseStatus.ConfirmCastration);
                break;
            case InventoryActionKind.CancelPractice:
                if (_approval is null) return;
                _approval = null; _message = "已取消修炼。"; AcknowledgeAndRender(); break;
            case InventoryActionKind.UseOnScene:
                if (_model?.CanUseOnScene != true || _itemId is not { } item) return;
                var session = _session!;
                Close();
                session.UseItem(item);
                _host.View.QueueRedraw();
                InputAcknowledged?.Invoke();
                break;
            case InventoryActionKind.Close: Close(); break;
        }
    }

    private void ApplyToRole(bool transfer, bool castration)
    {
        if (_session is not { CanSave: true } session || _itemId is not { } item || _roleId is not { } role || Mode(session, item) is not { } mode) return;
        var result = mode switch
        {
            ItemUseMode.Equip => session.EquipItem(item, role),
            ItemUseMode.Practice => session.PracticeItem(item, role, transfer, castration),
            _ => session.ConsumeItem(item, role)
        };
        _message = result.Message;
        _approval = result.Status is ExplorationItemUseStatus.ConfirmTransfer or ExplorationItemUseStatus.ConfirmCastration
            ? new(item, role, transfer, castration, result.Status, result.Message) : null;
        _host.View.QueueRedraw();
        AcknowledgeAndRender();
    }

    private void AcknowledgeAndRender()
    {
        _blockedFrame = Engine.GetProcessFrames();
        Render();
        InputAcknowledged?.Invoke();
    }

    private void Render()
    {
        if (_session is not { } session) return;
        if (_itemId is { } selected && session.Inventory.GetValueOrDefault(selected) <= 0) _itemId = null;
        if (_roleId is { } selectedRole && session.Party?.Contains(selectedRole) != true) _roleId = null;
        var items = session.Inventory.OrderBy(pair => pair.Key).Select(pair => new InventoryItemModel(pair.Key, ItemName(session, pair.Key), pair.Value));
        var roles = (session.Party?.Members.Where(id => id >= 0) ?? []).Select(id => new InventoryRoleModel(id, session.Party!.Roles[id].Name));
        var mode = _itemId is { } item ? Mode(session, item) : null;
        var target = _itemId is { } targetItem && mode is { } targetMode && _roleId is { } role
            ? session.GetItemUseTargets(targetItem, targetMode).FirstOrDefault(target => target.RoleId == role) : null;
        var apply = mode switch { ItemUseMode.Equip => "装备给所选人物", ItemUseMode.Practice => "让所选人物修炼",
            ItemUseMode.Consume => "让所选人物服用", _ => "对所选人物使用" };
        var context = new InventoryContext();
        _owner = new(context, session, ++_generation, _itemId, _roleId);
        var confirmation = _approval is { } approval ? new InventoryConfirmationModel(
            approval.Requested == ExplorationItemUseStatus.ConfirmTransfer ? "转移修炼物品" : "修炼条件", approval.Message) : null;
        _model = new(context, items, roles, _itemId, _roleId, DescribeItem(session), DescribeRole(session), _message,
            apply, target?.Eligible == true, target?.Reason ?? "", _itemId is not null && !session.IsWorld, confirmation);
        _shownRevision = session.PresentationRevision;
        _panel.Render(_model);
    }

    private string DescribeItem(GameSession session)
    {
        if (_itemId is not { } id) return "请选择行囊中的物品。";
        var type = Mode(session, id) switch { ItemUseMode.Equip => "装备", ItemUseMode.Practice => "修炼物品",
            ItemUseMode.Consume => "药品", _ => "剧情或其他物品" };
        var owner = session.Party?.ItemUsers.GetValueOrDefault(id, -1) ?? -1;
        var ownership = owner >= 0 && session.Party!.Roles.TryGetValue(owner, out var role) ? $"\n使用者：{role.Name}" : "";
        return $"{ItemName(session, id)} · {type}\n数量：{session.Inventory.GetValueOrDefault(id)}{ownership}";
    }

    private string DescribeRole(GameSession session)
    {
        if (_roleId is not { } id || session.Party?.Roles.TryGetValue(id, out var role) != true) return "当前没有可选择的队员。";
        return $"{role.Name}\n生命 {role[RoleWords.Hp]}/{role[RoleWords.MaximumHp]} · 内力 {role[RoleWords.Mp]}/{role[RoleWords.MaximumMp]}\n" +
            $"装备：{ItemName(session, role[RoleWords.EquipmentBegin])} / {ItemName(session, role[RoleWords.EquipmentBegin + 1])}\n" +
            $"修炼：{ItemName(session, role[RoleWords.PracticeItem])}";
    }

    private static ItemUseMode? Mode(GameSession session, int id) => session.BattleResources?.Items.TryGetValue(id, out var item) == true
        ? item[ItemWords.ItemType] switch { 1 => ItemUseMode.Equip, 2 => ItemUseMode.Practice, 3 => ItemUseMode.Consume, _ => null } : null;
    private static string ItemName(GameSession session, int id) => id < 0 ? "无" :
        session.Story?.ItemNames.GetValueOrDefault(id) ?? session.BattleResources?.Items.GetValueOrDefault(id)?.Name ?? $"物品 {id}";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true; _host.SessionChanged -= Close; _panel.IntentRequested -= HandleIntent; _panel.KeyInput -= PanelKeyInput;
        _session = null; _model = null; _owner = null; _approval = null; _generation++;
        _panel.Close();
    }
}
