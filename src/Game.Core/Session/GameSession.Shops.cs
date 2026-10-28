using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Shops;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    private readonly Dictionary<int, int[]> _shopStocks = [];
    private ShopDefinition? _activeShop;
    public string? ShopError { get; private set; }

    public ShopSession? Shop => !IsRetired && _activeShop is { } definition && Events?.PendingShop is { } request
        ? new(request.Token, definition.Id, definition.CurrencyItemId,
            definition.Offers.Select((offer, slot) => new ShopOffer(slot, offer.ItemId, offer.Price, Stock(definition)[slot]))
                .Where(offer => offer.Stock > 0).ToArray())
        : null;

    private int[] Stock(ShopDefinition definition)
    {
        if (!_shopStocks.TryGetValue(definition.Id, out var stock))
            _shopStocks.Add(definition.Id, stock = definition.Offers.Select(offer => offer.InitialStock).ToArray());
        return stock;
    }

    private ShopDefinition RequireCurrentShop() => _story?.Shops.Values.SingleOrDefault(shop => shop.SceneId == World.CurrentSceneId)
        ?? throw new InvalidOperationException("当前场景没有商店定义，请重新导入完整资源库。");

    private void ValidateShopCommand(EventCommand command)
    {
        if (command.Operation != EventOperation.Shop) return;
        var shop = RequireCurrentShop();
        if (IsWorld || _story?.ItemNames.ContainsKey(shop.CurrencyItemId) != true)
            throw new InvalidOperationException("商店货币资料不完整。");
        foreach (var index in ShopExitEvents(shop.SceneId)) _ = World.EventField(-2, index, 4);
    }

    void IEventWorld.BeginShop()
    {
        if (IsRetired || IsWorld || Battle is not null)
            throw new InvalidOperationException("A shop cannot open in the current mode.");
        // A previous failed event may have left its private definition behind;
        // only PendingShop grants transaction authority, so a new runner-owned
        // greeting can safely replace that inactive reference.
        _activeShop = RequireCurrentShop();
        _ = Stock(_activeShop);
        ShopError = null;
        PresentationRevision++;
    }

    public bool TryBuyShopItem(long token, int slot)
    {
        if (Shop is not { } menu || menu.Token != token || _activeShop is not { } definition) return false;
        var stock = Stock(definition);
        if (slot < 0 || slot >= definition.Offers.Count || stock[slot] <= 0)
        { ShopError = "这件物品已经售罄。"; return false; }
        var offer = definition.Offers[slot];
        var balance = _inventory.GetValueOrDefault(definition.CurrencyItemId);
        var purchased = _itemIds.Contains(definition.CurrencyItemId) && balance >= offer.Price;
        if (purchased)
        {
            // Calculate before committing, including MODs that sell the currency itself.
            var remainder = balance - offer.Price;
            var quantity = (long)(offer.ItemId == definition.CurrencyItemId ? remainder : _inventory.GetValueOrDefault(offer.ItemId)) + 1;
            if (quantity > int.MaxValue)
            { ShopError = "行囊中这件物品的数量已达上限。"; return false; }
            SetShopInventory(definition.CurrencyItemId, remainder);
            SetShopInventory(offer.ItemId, (int)quantity);
            stock[slot]--;
            PresentationRevision++;
        }
        ShopError = null;
        return Events!.ResolveShop(token, purchased);
    }

    private void SetShopInventory(int itemId, int quantity)
    {
        if (quantity == 0) { _inventory.Remove(itemId); _itemIds.Remove(itemId); }
        else { _inventory[itemId] = quantity; _itemIds.Add(itemId); }
    }

    public bool CloseShop(long token) => !IsRetired && Events?.CloseShop(token) == true;

    void IEventWorld.EndShop()
    {
        if (_activeShop is not { } definition) throw new InvalidOperationException("No shop is active.");
        // Z_DAT/0x312A6: keep the main merchant and arm auxiliary pass events939.
        foreach (var index in ShopExitEvents(definition.SceneId))
            World.ChangeEvent(World.CurrentSceneId, index, new GridPosition(-1, -1),
                [-2, index, 0, 0, -1, -1, 939, -1, -1, -1, -2, -2, -2]);
        _activeShop = null;
        ShopError = null;
        PresentationRevision++;
    }

    private static int[] ShopExitEvents(int sceneId) => sceneId switch
    {
        1 => [17, 18], 3 => [15, 16], 40 => [21, 22], 60 => [17, 18], 61 => [10, 11, 12], _ => []
    };

    private void RestoreShopStocks(IReadOnlyDictionary<int, int[]>? saved)
    {
        if (saved is null) return;
        if (saved.Count != (_story?.Shops.Count ?? 0)) throw new ArgumentException("Saved shop identities differ from the library.");
        foreach (var (id, stock) in saved)
        {
            if (_story is null || !_story.Shops.TryGetValue(id, out var definition) || stock is null || stock.Length != definition.Offers.Count ||
                stock.Where((quantity, slot) => quantity < 0 || quantity > definition.Offers[slot].InitialStock).Any())
                throw new ArgumentException("Saved shop stock is invalid.");
            _shopStocks.Add(id, stock.ToArray());
        }
    }
}
