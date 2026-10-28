namespace JinYong.Host;

/// <summary>Identity of one rendered shop menu, containing no session or game state.</summary>
public sealed class ShopActionToken
{
    internal ShopActionToken() { }
}

public enum ShopActionKind { Select, Buy, Cancel }
public sealed record ShopActionIntent(ShopActionToken Token, ShopActionKind Kind, int Slot = -1);
public sealed record ShopOfferModel(int Slot, int ItemId, string Name, int Price, int Stock);

/// <summary>A frozen projection of Core offers; the panel never prices or consumes items.</summary>
public sealed record ShopPanelModel
{
    public ShopPanelModel(ShopActionToken token, int balance, string currencyName,
        IReadOnlyList<ShopOfferModel> offers, string? error = null)
    {
        ArgumentNullException.ThrowIfNull(token);
        Token = token;
        Balance = balance;
        CurrencyName = currencyName;
        Offers = Array.AsReadOnly(offers.ToArray());
        Error = error;
    }

    public ShopActionToken Token { get; }
    public int Balance { get; }
    public string CurrencyName { get; }
    public IReadOnlyList<ShopOfferModel> Offers { get; }
    public string? Error { get; }
}
