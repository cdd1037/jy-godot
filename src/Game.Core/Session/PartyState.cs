using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace JinYong.Core.Session;

// OpenLegend include/openlegend/model/game_snapshot.hpp, namespace role_word.
// Offsets refer to 16-bit words in the original 182-byte role record.
public static class RoleWords
{
    public const int Count = 91, Id = 0, HeadId = 1, IncreasedLife = 2, Sex = 14, Level = 15,
        Experience = 16, Hp = 17, MaximumHp = 18, Hurt = 19, Poison = 20, PhysicalPower = 21,
        MakeItemExperience = 22, EquipmentBegin = 23, MpType = 40, Mp = 41, MaximumMp = 42,
        Attack = 43, Speed = 44, Defence = 45, Medicine = 46, UsePoison = 47,
        Detoxification = 48, AntiPoison = 49, Fist = 50, Sword = 51, Knife = 52,
        Unusual = 53, HiddenWeapon = 54, Knowledge = 55, Morality = 56,
        AttackWithPoison = 57, AttackTwice = 58, Fame = 59, Iq = 60, PracticeItem = 61,
        ItemExperience = 62, MagicIdBegin = 63, MagicLevelBegin = 73, MagicCount = 10,
        TakingItemBegin = 83, TakingCountBegin = 87, TakingItemCount = 4;

    public static bool IsAttribute(int word) => word is 2 or 3 or >= 14 and <= 90;
}

/// <summary>Immutable imported definition; Words preserve all 91 signed legacy words.</summary>
public sealed class RoleDefinition
{
    [JsonConstructor]
    public RoleDefinition(int id, string name, string nickname, IReadOnlyList<int> words)
    {
        if (id < 0 || words.Count != RoleWords.Count || words.Any(word => word is < short.MinValue or > short.MaxValue))
            throw new ArgumentException("Role definition requires a valid ID and 91 signed 16-bit words.");
        if (words[RoleWords.Id] != id) throw new ArgumentException("Role ID differs from legacy record.");
        Id = id; Name = name; Nickname = nickname;
        Words = Array.AsReadOnly(words.ToArray());
    }
    public int Id { get; }
    public string Name { get; }
    public string Nickname { get; }
    public IReadOnlyList<int> Words { get; }
}

public sealed class PartyDefinition
{
    [JsonConstructor]
    public PartyDefinition(IReadOnlyList<RoleDefinition> roles, IReadOnlyList<int> members,
        IReadOnlyDictionary<int, int>? itemUsers = null)
    {
        if (members.Count != PartyState.Capacity || roles.Select(role => role.Id).Distinct().Count() != roles.Count)
            throw new ArgumentException("Party requires six member slots and unique role definitions.");
        var ids = roles.Select(role => role.Id).ToHashSet();
        if (members[0] != 0 || !ids.Contains(0) || members.Any(id => id >= 0 && !ids.Contains(id)))
            throw new ArgumentException("Party references a missing role or protagonist.");
        if (members.Where(id => id >= 0).Distinct().Count() != members.Count(id => id >= 0))
            throw new ArgumentException("Party contains duplicate members.");
        if (members.Skip(1).Any(id => id == 0 || id < -1)) throw new ArgumentException("Invalid companion slot.");
        var firstEmpty = members.ToList().IndexOf(-1);
        if (firstEmpty >= 0 && members.Skip(firstEmpty).Any(id => id != -1))
            throw new ArgumentException("Party members must occupy contiguous slots.");
        Roles = Array.AsReadOnly(roles.ToArray());
        Members = Array.AsReadOnly(members.ToArray());
        ItemUsers = new ReadOnlyDictionary<int, int>(new Dictionary<int, int>(itemUsers ?? new Dictionary<int, int>()));
    }
    public IReadOnlyList<RoleDefinition> Roles { get; }
    public IReadOnlyList<int> Members { get; }
    public IReadOnlyDictionary<int, int> ItemUsers { get; }
}

public sealed class RoleState
{
    private readonly int[] _words;
    internal RoleState(RoleDefinition definition)
    {
        Definition = definition; _words = definition.Words.ToArray();
        // Experience and magic levels are unsigned in the original representation.
        foreach (var word in new[] { RoleWords.Experience, RoleWords.MakeItemExperience, RoleWords.ItemExperience }
            .Concat(Enumerable.Range(RoleWords.MagicLevelBegin, RoleWords.MagicCount)))
            _words[word] = unchecked((ushort)_words[word]);
        for (var slot = 0; slot < RoleWords.TakingItemCount; slot++)
            if (_words[RoleWords.TakingItemBegin + slot] == -1) _words[RoleWords.TakingCountBegin + slot] = 0;
    }
    public RoleDefinition Definition { get; }
    public int Id => Definition.Id;
    public string Name => Definition.Name;
    public int this[int word] => _words[word];
    internal void Set(int word, int value) => _words[word] = value;
}

public readonly record struct PartyItemTransfer(int ItemId, int Count);
public sealed record PartyJoinResult(bool Joined, IReadOnlyList<PartyItemTransfer> Items);

/// <summary>
/// Runtime copy of imported party/roles, independent of Godot and inventory presentation.
/// Rules follow OpenLegend src/scene/scene.cpp opcodes 10–63 and its role helpers.
/// Deliberate normalization: duplicate/full joins have no side effects; arithmetic is
/// checked 32-bit instead of DOS signed-word wrap. No new-game-plus attribute rules.
/// </summary>
public sealed partial class PartyState
{
    public const int Capacity = 6;
    private readonly int[] _members;
    private readonly Dictionary<int, RoleState> _roles;
    private readonly Dictionary<int, int> _itemUsers;
    public PartyState(PartyDefinition definition)
    {
        _members = definition.Members.ToArray();
        _roles = definition.Roles.ToDictionary(role => role.Id, role => new RoleState(role));
        _itemUsers = new Dictionary<int, int>(definition.ItemUsers);
        Members = Array.AsReadOnly(_members);
        Roles = new ReadOnlyDictionary<int, RoleState>(_roles);
        ItemUsers = new ReadOnlyDictionary<int, int>(_itemUsers);
    }
    public IReadOnlyList<int> Members { get; }
    public IReadOnlyDictionary<int, RoleState> Roles { get; }
    public IReadOnlyDictionary<int, int> ItemUsers { get; }
    public bool IsFull => _members[^1] > 0;
    public bool HasFemale => _members.Any(id => id >= 0 && GetWord(id, RoleWords.Sex) == 1);
    public bool Contains(int roleId) => _members.Contains(roleId) && roleId >= 0;
    private RoleState Role(int roleId) => _roles.TryGetValue(roleId, out var role) ? role :
        throw new ArgumentOutOfRangeException(nameof(roleId), roleId, "Unknown role.");
    public int GetWord(int roleId, int word)
    {
        if (word < 0 || word >= RoleWords.Count) throw new ArgumentOutOfRangeException(nameof(word));
        return Role(roleId)[word];
    }
    public void SetWord(int roleId, int word, int value)
    {
        if (!RoleWords.IsAttribute(word)) throw new ArgumentOutOfRangeException(nameof(word), "Role identity/name bytes are immutable.");
        Role(roleId).Set(word, value);
    }
    public int AddAttribute(int roleId, int word, int delta)
    {
        var role = Role(roleId);
        if (!RoleWords.IsAttribute(word)) throw new ArgumentOutOfRangeException(nameof(word));
        var after = checked(role[word] + delta);
        if (word is RoleWords.Iq or RoleWords.Speed or RoleWords.Morality) after = Math.Clamp(after, 0, 100);
        if (word == RoleWords.Attack) after = Math.Max(0, after);
        if (word is RoleWords.MaximumHp or RoleWords.MaximumMp && after < 0)
            throw new ArgumentOutOfRangeException(nameof(delta), "Maximum HP/MP cannot be negative.");
        var before = word == RoleWords.MaximumHp ? role[RoleWords.Hp] :
            word == RoleWords.MaximumMp ? role[RoleWords.Mp] : role[word];
        var gain = checked(after - before);
        role.Set(word, after);
        if (word == RoleWords.MaximumHp) role.Set(RoleWords.Hp, after);
        if (word == RoleWords.MaximumMp) role.Set(RoleWords.Mp, after);
        return gain;
    }
    public PartyJoinResult Join(int roleId)
    {
        var role = Role(roleId);
        var slot = Array.IndexOf(_members, -1);
        if (Contains(roleId) || slot < 1) return new(false, []);
        var items = new List<PartyItemTransfer>();
        for (var i = 0; i < RoleWords.TakingItemCount; i++)
        {
            var id = role[RoleWords.TakingItemBegin + i];
            if (id >= 0) items.Add(new(id, role[RoleWords.TakingCountBegin + i]));
        }
        _members[slot] = roleId;
        for (var i = 0; i < RoleWords.TakingItemCount; i++)
        {
            role.Set(RoleWords.TakingItemBegin + i, -1);
            role.Set(RoleWords.TakingCountBegin + i, 0);
        }
        ClearPersonalItems(role, releaseUsers: false);
        return new(true, items.AsReadOnly());
    }
    public bool Leave(int roleId)
    {
        var role = Role(roleId);
        if (roleId == 0) return false;
        var index = Array.IndexOf(_members, roleId);
        if (index > 0)
        {
            Array.Copy(_members, index + 1, _members, index, Capacity - index - 1);
            _members[^1] = -1;
        }
        ClearPersonalItems(role, releaseUsers: true);
        return index > 0;
    }
    private void ClearPersonalItems(RoleState role, bool releaseUsers)
    {
        foreach (var word in new[] { RoleWords.EquipmentBegin, RoleWords.EquipmentBegin + 1, RoleWords.PracticeItem })
        {
            if (releaseUsers && role[word] >= 0 && _itemUsers.ContainsKey(role[word])) _itemUsers[role[word]] = -1;
            role.Set(word, -1);
        }
        role.Set(RoleWords.ItemExperience, 0);
    }
    public void DismissCompanions()
    {
        foreach (var id in _members.Skip(1).Where(id => id > 0).ToArray()) Leave(id);
    }
    public IReadOnlyList<int> Rest()
    {
        var recovered = new List<int>();
        foreach (var id in _members.Where(id => id >= 0))
        {
            var role = Role(id);
            // Baseline original: hurt < 33 and no poison; hurt == 33 cannot rest.
            if (role[RoleWords.Hurt] >= 33 || role[RoleWords.Poison] != 0) continue;
            role.Set(RoleWords.Hurt, 0); role.Set(RoleWords.PhysicalPower, 100);
            role.Set(RoleWords.Hp, role[RoleWords.MaximumHp]); role.Set(RoleWords.Mp, role[RoleWords.MaximumMp]);
            recovered.Add(id);
        }
        return recovered.AsReadOnly();
    }
    public void ClearMp()
    {
        foreach (var id in _members.Where(id => id >= 0)) Role(id).Set(RoleWords.Mp, 0);
    }
    public int LearnMagic(int roleId, int magicId) => SetMagic(roleId, -1, magicId, 0);
    public int SetMagic(int roleId, int slot, int magicId, int level)
    {
        var role = Role(roleId);
        if (slot == -1)
        {
            // Original opcode 33/35: first empty (ID 0); full list overwrites slot 0.
            slot = 0;
            for (var i = 0; i < RoleWords.MagicCount; i++)
                if (role[RoleWords.MagicIdBegin + i] == 0) { slot = i; break; }
        }
        if (slot < 0 || slot >= RoleWords.MagicCount) throw new ArgumentOutOfRangeException(nameof(slot));
        role.Set(RoleWords.MagicIdBegin + slot, magicId); role.Set(RoleWords.MagicLevelBegin + slot, level);
        return slot;
    }
    public bool AddRoleItem(int roleId, int itemId, int count)
    {
        if (itemId < 0) throw new ArgumentOutOfRangeException(nameof(itemId));
        var role = Role(roleId);
        var slot = -1;
        for (var i = 0; i < RoleWords.TakingItemCount; i++)
            if (role[RoleWords.TakingItemBegin + i] == itemId) { slot = i; break; }
        if (slot < 0)
            for (var i = 0; i < RoleWords.TakingItemCount; i++)
                if (role[RoleWords.TakingItemBegin + i] == -1) { slot = i; break; }
        if (slot < 0) return false;
        var after = role[RoleWords.TakingItemBegin + slot] == itemId ?
            checked(role[RoleWords.TakingCountBegin + slot] + count) : count;
        // Original carried-item slots retain IDs at zero/negative quantities.
        role.Set(RoleWords.TakingItemBegin + slot, itemId); role.Set(RoleWords.TakingCountBegin + slot, after);
        return true;
    }
}
