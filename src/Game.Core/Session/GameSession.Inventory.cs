using JinYong.Core.Battles;

namespace JinYong.Core.Session;

public sealed partial class GameSession : IBattleInventory
{
    IReadOnlyDictionary<int, int> IBattleInventory.Items => Inventory;

    bool IBattleInventory.TryConsume(int itemId, int count) => TryConsumeInventoryItem(itemId, count);

    private bool TryConsumeInventoryItem(int itemId, int count)
    {
        if (IsRetired || count <= 0 || !_inventory.TryGetValue(itemId, out var available) || available < count) return false;
        var remaining = available - count;
        if (remaining == 0) _inventory.Remove(itemId); else _inventory[itemId] = remaining;
        // Consumed slots disappear, matching normal inventory adjustments;
        // unrelated legacy zero-quantity ID markers remain untouched.
        if (remaining == 0) _itemIds.Remove(itemId); else _itemIds.Add(itemId);
        PresentationRevision++;
        return true;
    }
}
