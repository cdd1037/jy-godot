using Godot;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Hosts one pending Core shop without owning stock, prices or event continuation.</summary>
public sealed class ShopController : IDisposable
{
    private readonly GameViewHost _host;
    private readonly ShopPanel _panel;
    private GameSession? _session;
    private long? _shopToken;
    private ShopActionToken? _actionToken;
    private ShopPanelModel? _model;
    private ulong? _openedFrame, _closedFrame;
    private bool _submitting, _disposed;

    public bool IsOpen => _panel.IsOpen;
    public event Action? InputAcknowledged;

    public ShopController(GameViewHost host, ShopPanel panel)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _panel = panel ?? throw new ArgumentNullException(nameof(panel));
        _host.SessionChanged += Reset;
        _panel.ActionRequested += HandleIntent;
    }

    public void Advance()
    {
        if (_disposed) return;
        var session = _host.Session;
        // The entry and outcome dialogues belong to the existing runner. Only its
        // pending shop token authorizes a menu; a retained shop definition does not.
        if (session?.Shop is not { } shop || session.Events?.PendingShop?.Token != shop.Token)
        {
            CloseView();
            return;
        }
        if (!ReferenceEquals(_session, session) || _shopToken != shop.Token)
        {
            CloseView();
            _session = session;
            _shopToken = shop.Token;
            _actionToken = new();
            _openedFrame = Engine.GetProcessFrames();
        }
        var model = ShopModelFactory.Build(session, shop, _actionToken!);
        if (_model is not null && Equivalent(_model, model)) return;
        _model = model;
        _panel.Render(model);
    }

    public bool HandleKeyInput(InputEvent input)
    {
        if (_disposed) return false;
        var frame = Engine.GetProcessFrames();
        if (!IsOpen) return _closedFrame == frame;
        if (_openedFrame != frame) _panel.HandleKeyInput(input);
        return true;
    }

    private void HandleIntent(ShopActionIntent intent)
    {
        if (_disposed || _submitting || !IsOpen || !ReferenceEquals(intent.Token, _actionToken) ||
            !ReferenceEquals(_host.Session, _session) || _session is not { } session ||
            session.Shop is not { } shop || shop.Token != _shopToken ||
            session.Events?.PendingShop?.Token != shop.Token || _openedFrame == Engine.GetProcessFrames() ||
            _closedFrame == Engine.GetProcessFrames()) return;
        if (intent.Kind == ShopActionKind.Select) return;
        if (intent.Kind is not (ShopActionKind.Buy or ShopActionKind.Cancel)) return;
        _submitting = true;
        _panel.SetSubmitting(true);
        try
        {
            var accepted = intent.Kind == ShopActionKind.Buy
                ? session.TryBuyShopItem(shop.Token, intent.Slot) : session.CloseShop(shop.Token);
            if (accepted) CloseView();
            // A rejected stale/invalid slot stays a Core error; no simulated purchase
            // or balance check is performed here. Accepted insufficient funds closes.
            Advance();
        }
        finally
        {
            _submitting = false;
            if (IsOpen) _panel.SetSubmitting(false);
        }
    }

    private static bool Equivalent(ShopPanelModel left, ShopPanelModel right) =>
        ReferenceEquals(left.Token, right.Token) && left.Balance == right.Balance &&
        left.CurrencyName == right.CurrencyName && left.Error == right.Error && left.Offers.SequenceEqual(right.Offers);

    private void Reset() => CloseView();

    private void CloseView()
    {
        var wasOpen = IsOpen;
        _panel.Close();
        _session = null;
        _shopToken = null;
        _actionToken = null;
        _model = null;
        _openedFrame = null;
        if (!wasOpen) return;
        _closedFrame = Engine.GetProcessFrames();
        InputAcknowledged?.Invoke();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.SessionChanged -= Reset;
        _panel.ActionRequested -= HandleIntent;
        _panel.Close();
        _session = null;
        _model = null;
    }
}
