using System.Text.Json.Serialization;

namespace JinYong.Core.Shops;

public sealed record ShopOfferDefinition(int ItemId, int InitialStock, int Price);

/// <summary>Immutable stock baseline. DOS imports preserve all five physical slots.</summary>
public sealed class ShopDefinition
{
    [JsonConstructor]
    public ShopDefinition(int id, int sceneId, IReadOnlyList<ShopOfferDefinition> offers, int currencyItemId = 174)
    {
        ArgumentNullException.ThrowIfNull(offers);
        if (id < 0 || sceneId < 0 || currencyItemId < 0 || offers.Count is < 1 or > 200 ||
            offers.Any(offer => offer is null || offer.ItemId < 0 || offer.InitialStock < 0 || offer.Price < 0))
            throw new ArgumentException("Invalid shop identity, stock or price.");
        Id = id;
        SceneId = sceneId;
        CurrencyItemId = currencyItemId;
        Offers = Array.AsReadOnly(offers.ToArray());
    }

    public int Id { get; }
    public int SceneId { get; }
    public int CurrencyItemId { get; }
    public IReadOnlyList<ShopOfferDefinition> Offers { get; }
}
