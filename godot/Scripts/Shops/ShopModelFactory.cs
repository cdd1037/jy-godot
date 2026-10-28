using JinYong.Core.Session;
using JinYong.Core.Shops;

namespace JinYong.Host;

/// <summary>Reads display data only; acceptance, stock and currency rules remain in Core.</summary>
public static class ShopModelFactory
{
    public static ShopPanelModel Build(GameSession session, ShopSession shop, ShopActionToken token)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(shop);
        ArgumentNullException.ThrowIfNull(token);
        string Name(int id) => session.Story?.ItemNames.GetValueOrDefault(id) ?? $"物品 {id}";
        return new(token, session.Inventory.GetValueOrDefault(shop.CurrencyItemId), Name(shop.CurrencyItemId),
            shop.Offers.Select(offer => new ShopOfferModel(offer.Slot, offer.ItemId,
                Name(offer.ItemId), offer.Price, offer.Stock)).ToArray(), session.ShopError);
    }
}
