using System.Collections.ObjectModel;
using JinYong.Content.Packages;

namespace JinYong.Content.Library;

public sealed partial class GameLibrary
{
    public ScenePackage OpenWorldView(JinYong.Core.Session.GameSession session)
    {
        if (MainMap is null || !Sprites.Banks.TryGetValue("world", out var mapping))
            throw new InvalidOperationException("World travel resources are missing; reimport the complete library.");
        var document = new SceneDocument
        {
            FormatVersion = 3, Id = "base:world", LegacySceneId = -1, Name = "江湖世界",
            Layers = Manifest.MainMap!, Palette = Manifest.Palette,
            Sprites = mapping.OrderBy(pair => pair.Key).Select(pair => Sprites.Assets[pair.Value] with { LegacyId = pair.Key }).ToArray(),
            Portraits = Sprites.Banks["portrait"].OrderBy(pair => pair.Key).Select(pair => Sprites.Assets[pair.Value] with { LegacyId = pair.Key }).ToArray(),
            PortraitNames = GameData.PortraitNames, Sounds = Audio.Effects, Story = Manifest.GameData
        };
        return new(Directory, new PackageManifest
        {
            FormatVersion = 3, Id = Manifest.Id, EngineApi = Manifest.EngineApi, Version = Manifest.Version,
            Scene = Manifest.SceneIndex, FileHashes = Manifest.FileHashes, SourceHashes = Manifest.SourceHashes
        }, document, session.Definition, session.Story) { Library = this };
    }

    public ScenePackage OpenBattleView(JinYong.Core.Session.GameSession session)
    {
        var battle = session.Battle ?? throw new InvalidOperationException("No battle is active.");
        var bank = $"battlefield/{battle.Battlefield.Id:D3}";
        if (!Sprites.Banks.TryGetValue(bank, out var mapping)) throw new InvalidDataException($"Missing battle sprite bank {bank}.");
        var animationBanks = new Dictionary<string, SpriteAsset[]>(StringComparer.Ordinal);
        void AnimationBank(string name)
        {
            if (!Sprites.Banks.TryGetValue(name,out var entries))
                throw new InvalidDataException($"Missing battle animation bank {name}; reimport the complete resource library.");
            animationBanks.Add(name,entries.OrderBy(pair=>pair.Key)
                .Select(pair=>Sprites.Assets[pair.Value] with { LegacyId=pair.Key }).ToArray());
        }
        AnimationBank("effect");
        // Enemy slots are not appended until party confirmation. Load their
        // banks now, together with every selectable ally and fixed/preset role.
        var roles = battle.SelectableRoleIds.Concat(battle.Combatants.Select(actor=>actor.RoleId))
            .Concat(Enumerable.Range(0,6).Select(battle.Definition.FixedParty))
            .Concat(Enumerable.Range(0,6).Select(battle.Definition.PresetParty))
            .Concat(Enumerable.Range(0,20).Select(battle.Definition.EnemyRole)).Where(role=>role>=0).Distinct();
        var heads = roles.Select(role=>session.Party!.Roles[role][JinYong.Core.Session.RoleWords.HeadId]).Distinct().Order();
        foreach(var head in heads) AnimationBank($"fight/{head:D3}");
        var document = new SceneDocument
        {
            FormatVersion = 3, Id = $"base:battle/{battle.Definition.Id:D3}", LegacySceneId = -2,
            Name = battle.Definition.Name, Layers = Manifest.Battlefields!, Palette = Manifest.Palette,
            Sprites = mapping.OrderBy(pair => pair.Key).Select(pair => Sprites.Assets[pair.Value] with { LegacyId = pair.Key }).ToArray(),
            Portraits = Sprites.Banks["portrait"].OrderBy(pair => pair.Key).Select(pair => Sprites.Assets[pair.Value] with { LegacyId = pair.Key }).ToArray(),
            PortraitNames = GameData.PortraitNames, Sounds = Audio.Effects, Story = Manifest.GameData,
            BattleSpriteBanks = new ReadOnlyDictionary<string,SpriteAsset[]>(animationBanks)
        };
        return new(Directory, new PackageManifest
        {
            FormatVersion = 3, Id = Manifest.Id, EngineApi = Manifest.EngineApi, Version = Manifest.Version,
            Scene = Manifest.SceneIndex, FileHashes = Manifest.FileHashes, SourceHashes = Manifest.SourceHashes
        }, document, session.Definition, session.Story) { Library = this };
    }
}
