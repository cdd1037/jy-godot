using System.Collections.ObjectModel;
using JinYong.Core.Session;

namespace JinYong.Core.Battles;

/// <summary>Shared immutable battle resources; mutable combat and party state stay in the session.</summary>
public sealed class BattleCatalog
{
    public BattleCatalog(IEnumerable<BattleDefinition> battles, IEnumerable<BattlefieldDefinition> fields,
        IEnumerable<MagicDefinition> magics, IEnumerable<ItemDefinition> items, PartyDefinition? party = null,
        ProgressionDefinition? progression = null)
    {
        Definitions = new ReadOnlyDictionary<int, BattleDefinition>(battles.ToDictionary(value => value.Id));
        Fields = new ReadOnlyDictionary<int, BattlefieldDefinition>(fields.ToDictionary(value => value.Id));
        Magics = new ReadOnlyDictionary<int, MagicDefinition>(magics.ToDictionary(value => value.Id));
        Items = new ReadOnlyDictionary<int, ItemDefinition>(items.ToDictionary(value => value.Id));
        Progression = progression;
        foreach (var battle in Definitions.Values)
        {
            if (!Fields.ContainsKey(battle.BattlefieldId)) throw new ArgumentException($"Battle {battle.Id} has no battlefield.");
            void RequireRole(int role)
            {
                if (role < -1 || party is not null && role >= 0 && !party.Roles.Any(value => value.Id == role))
                    throw new ArgumentException($"Battle {battle.Id} references unknown role {role}.");
            }
            for (var slot = 0; slot < BattleWords.PartySlots; slot++)
            {
                RequireRole(battle.PresetParty(slot)); RequireRole(battle.FixedParty(slot));
                if ((battle.PresetParty(slot) >= 0 || battle.FixedParty(slot) >= 0) && !BattlefieldDefinition.Contains(battle.PartyPosition(slot)))
                    throw new ArgumentException($"Battle {battle.Id} has an invalid allied position.");
            }
            for (var slot = 0; slot < BattleWords.EnemySlots; slot++)
            {
                RequireRole(battle.EnemyRole(slot));
                if (battle.EnemyRole(slot) >= 0 && !BattlefieldDefinition.Contains(battle.EnemyPosition(slot)))
                    throw new ArgumentException($"Battle {battle.Id} has an invalid enemy position.");
            }
        }
    }
    public IReadOnlyDictionary<int, BattleDefinition> Definitions { get; }
    public IReadOnlyDictionary<int, BattlefieldDefinition> Fields { get; }
    public IReadOnlyDictionary<int, MagicDefinition> Magics { get; }
    public IReadOnlyDictionary<int, ItemDefinition> Items { get; }
    public ProgressionDefinition? Progression { get; }
}
