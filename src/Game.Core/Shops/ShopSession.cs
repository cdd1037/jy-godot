namespace JinYong.Core.Shops;

public sealed record ShopOffer(int Slot, int ItemId, int Price, int Stock);

/// <summary>Read-only menu snapshot owned by one event continuation, not a second script runtime.</summary>
public sealed class ShopSession(long token, int shopId, int currencyItemId, IReadOnlyList<ShopOffer> offers)
{
    public long Token { get; } = token;
    public int ShopId { get; } = shopId;
    public int CurrencyItemId { get; } = currencyItemId;
    public IReadOnlyList<ShopOffer> Offers { get; } = Array.AsReadOnly(offers.ToArray());
}
