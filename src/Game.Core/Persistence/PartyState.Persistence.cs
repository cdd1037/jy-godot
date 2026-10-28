using JinYong.Core.Persistence;

namespace JinYong.Core.Session;

public sealed partial class PartyState
{
    internal PartySnapshot CaptureState() => new(_members.ToArray(),
        _roles.ToDictionary(pair => pair.Key, pair => Enumerable.Range(0, RoleWords.Count).Select(word => pair.Value[word]).ToArray()),
        new Dictionary<int, int>(_itemUsers));

    internal void RestoreState(PartySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Members is null || snapshot.Roles is null || snapshot.ItemUsers is null ||
            snapshot.Roles.Count != _roles.Count || snapshot.Roles.Keys.Any(id => !_roles.ContainsKey(id)))
            throw new ArgumentException("Saved party does not match the role definitions.");
        _ = new PartyDefinition(_roles.Values.Select(role => role.Definition).ToArray(), snapshot.Members, snapshot.ItemUsers);
        if (snapshot.ItemUsers.Any(pair => pair.Key < 0 || pair.Value != -1 && !_roles.ContainsKey(pair.Value)))
            throw new ArgumentException("Invalid saved item ownership.");
        foreach (var (id, words) in snapshot.Roles)
        {
            if (words is null || words.Length != RoleWords.Count) throw new ArgumentException("Invalid saved role record.");
            for (var word = 0; word < words.Length; word++)
            {
                // The imported baseline is signed-word data, but normalized runtime operations
                // deliberately retain checked 32-bit integers. A checkpoint must not narrow them.
                if (!RoleWords.IsAttribute(word) && words[word] != _roles[id].Definition.Words[word])
                    throw new ArgumentException("Saved role identity differs from the definition.");
            }
        }
        // Validate every record before mutating this candidate.
        Array.Copy(snapshot.Members, _members, Capacity);
        foreach (var (id, words) in snapshot.Roles)
            for (var word = 0; word < words.Length; word++) _roles[id].Set(word, words[word]);
        _itemUsers.Clear(); foreach (var pair in snapshot.ItemUsers) _itemUsers.Add(pair.Key, pair.Value);
    }
}
