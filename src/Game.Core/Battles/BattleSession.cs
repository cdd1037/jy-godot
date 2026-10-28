using JinYong.Core.Session;

namespace JinYong.Core.Battles;

/// <summary>
/// Host-independent tactical session. Native contracts: OpenLegend battle_setup.cpp,
/// battle_pathing.cpp and research/evidence/battle-1to1.md §§6–8,18, sub_3C563.
/// Friendly state is shared; enemy baselines are isolated from the party. No
/// pre-battle party healing, invented normal attack, or automatic victory.
/// </summary>
public sealed partial class BattleSession
{
    public const int Capacity = 26;
    public const string AiPolicyDescription = "DOS action priorities, carried items, support, packed magic and role-based targeting with movement continuations; unsafe legacy indices and path aliases are rejected.";
    private readonly PartyState _party;
    private readonly IReadOnlyDictionary<int, MagicDefinition> _magics;
    private readonly IReadOnlyDictionary<int, ItemDefinition> _items;
    private readonly List<BattleCombatant> _combatants = [];
    private readonly Dictionary<int, RoleState> _enemies = [];
    private readonly Func<int, int> _random;
    private readonly IBattleInventory? _battleInventory;
    private readonly HashSet<int> _mandatory = [];
    private readonly int[] _occupancy = Enumerable.Repeat(-1, 4096).ToArray();
    private int _actor;
    private uint _randomState;
    private bool _rosterComplete, _settlementCompleted;

    public BattleSession(BattleDefinition definition, BattlefieldDefinition battlefield, PartyState party,
        IReadOnlyDictionary<int, MagicDefinition> magics, IReadOnlyDictionary<int, ItemDefinition>? items = null,
        uint randomSeed = 1, int initialCostScale = 0, Func<int, int>? random = null, IBattleInventory? inventory = null,
        bool presentActions = false)
    {
        if (definition.BattlefieldId != battlefield.Id) throw new ArgumentException("Battlefield ID does not match the battle definition.");
        Definition = definition; Battlefield = battlefield; _party = party; _magics = magics;
        _items = items ?? new Dictionary<int, ItemDefinition>();
        _battleInventory = inventory;
        _presentActions = presentActions;
        _randomState = randomSeed;
        _random = bound =>
        {
            if (bound <= 1 || bound > 30000) return 0;
            var value = random is null ? DrawRandom(bound) : random(bound);
            return value >= 0 && value < bound ? value : throw new ArgumentException("Battle random source returned an invalid value.");
        };
        HpCostScale = initialCostScale;
        Combatants = _combatants.AsReadOnly();
        SelectableRoleIds = Array.AsReadOnly(party.Members.Where(id => id >= 0).ToArray());
        if (Enumerable.Range(0, 6).Any(slot => definition.FixedParty(slot) != -1))
        {
            for (var slot = 0; slot < 6; slot++)
                if (definition.FixedParty(slot) != -1) Append(definition.FixedParty(slot), true, definition.PartyPosition(slot));
            AppendEnemies(); BeginRound();
        }
        else
        {
            for (var slot = 0; slot < 6; slot++)
                if (definition.PresetParty(slot) != -1)
                {
                    var id = definition.PresetParty(slot); _mandatory.Add(id);
                    Append(id, true, definition.PartyPosition(slot));
                }
            Phase = BattlePhase.PartySelection;
        }
        MandatoryRoleIds = Array.AsReadOnly(_mandatory.Order().ToArray());
    }
    public BattleDefinition Definition { get; }
    public BattlefieldDefinition Battlefield { get; }
    public IReadOnlyList<BattleCombatant> Combatants { get; }
    public IReadOnlyList<int> SelectableRoleIds { get; }
    public IReadOnlyList<int> MandatoryRoleIds { get; }
    public BattlePhase Phase { get; private set; }
    public BattleOutcome Outcome { get; private set; }
    public BattleCombatant? CurrentActor => Phase == BattlePhase.Acting && _actor < _combatants.Count ? _combatants[_actor] : null;
    public int Round { get; private set; }
    public int HpCostScale { get; private set; }
    public uint RandomState => _randomState;
    public string? LastError { get; private set; }
    public IReadOnlyList<BattleDamageEvent> LastDamage { get; private set; } = [];

    private int DrawRandom(int bound)
    {
        if (bound <= 1 || bound > 30000) return 0;
        _randomState = unchecked(_randomState * 0x41C64E6D + 0x3039);
        return (int)((_randomState >> 16) & 0x7FFF) % bound;
    }
    private bool Fail(string message) { LastError = message; return false; }
}
