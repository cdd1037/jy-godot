using System.Buffers.Binary;
using JinYong.Core.Shops;

namespace JinYong.Importing.Legacy;

/// <summary>
/// RANGER entry 5: five 30-byte records, each containing five item IDs,
/// five stock words and five price words (signed little-endian int16).
/// Source: OpenLegend include/openlegend/model/game_snapshot.hpp shop_word and
/// research/evidence/functions/Z_DAT/0x312A6.md. Partial fixtures may provide
/// the first N records; omitted shops are never synthesized.
/// </summary>
public static class LegacyShopImporter
{
    private static readonly int[] SceneIds = [1, 3, 40, 60, 61];
    private const int RecordBytes = 30, OfferCount = 5;

    public static ShopDefinition[] Read(LegacyArchive ranger, IReadOnlyDictionary<int, string> itemNames)
    {
        ArgumentNullException.ThrowIfNull(ranger);
        ArgumentNullException.ThrowIfNull(itemNames);
        var bytes = ranger.Entry(5);
        if (bytes.Length % RecordBytes != 0 || bytes.Length / RecordBytes > SceneIds.Length)
            throw new InvalidDataException("RANGER shops require at most five complete 30-byte records.");
        var shops = new ShopDefinition[bytes.Length / RecordBytes];
        for (var shop = 0; shop < shops.Length; shop++)
        {
            var offers = new ShopOfferDefinition[OfferCount];
            var record = bytes.Slice(shop * RecordBytes, RecordBytes);
            for (var slot = 0; slot < OfferCount; slot++)
            {
                var item = BinaryPrimitives.ReadInt16LittleEndian(record[(slot * 2)..]);
                var stock = BinaryPrimitives.ReadInt16LittleEndian(record[((OfferCount + slot) * 2)..]);
                var price = BinaryPrimitives.ReadInt16LittleEndian(record[((2 * OfferCount + slot) * 2)..]);
                if (!itemNames.ContainsKey(item))
                    throw new InvalidDataException($"Shop {shop}, slot {slot} references unknown item {item}.");
                offers[slot] = new(item, stock, price);
            }
            shops[shop] = new(shop, SceneIds[shop], offers);
        }
        return shops;
    }
}
