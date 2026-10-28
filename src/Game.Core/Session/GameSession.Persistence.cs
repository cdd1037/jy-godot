using JinYong.Core.Battles;
using JinYong.Core.Events;
using JinYong.Core.Maps;
using JinYong.Core.Persistence;

namespace JinYong.Core.Session;

public sealed partial class GameSession
{
    /// <summary>Stable exploration only; no suspended script, battle, fade transition or unprocessed arrival.</summary>
    public bool CanSave => !IsRetired && !IsInputLocked && !_arrivalPending && !_resolveAfterEvent && ScriptedMotion is null &&
        (Events?.CanCaptureIdleState ?? true);

    public SessionSnapshot CaptureSnapshot()
    {
        if (!CanSave) throw new InvalidOperationException("The session is not at a stable exploration checkpoint.");
        return new SessionSnapshot
        {
            SceneId = World.CurrentSceneId, DefinitionId = Definition.Id, IsWorld = IsWorld, Position = _position, Facing = _facing, Steps = _steps,
            PictureOverride = _picture, WalkFrameOffset = _walkFrameOffset, ShipFrameOffset = _shipFrameOffset,
            Travel = Travel?.Snapshot, LastWorldEntryDirection = _lastWorldEntryDirection, TravelToken = _travelToken,
            HpCostScale = _battleHpCostScale, Camera = Camera, FadeOpacity = FadeOpacity,
            Party = Party?.CaptureState(), Inventory = new Dictionary<int, int>(_inventory), ItemIds = _itemIds.Order().ToArray(),
            Variables = new Dictionary<string, int>(_variables, StringComparer.Ordinal), Scenes = World.CaptureState(),
            Events = Events?.CaptureIdleState(),
            ShopStocks = (_story?.Shops.Values ?? []).ToDictionary(shop => shop.Id, shop => Stock(shop).ToArray()),
            CurrentMusicId = CurrentMusicId, ExitMusicOverride = _exitMusicOverride
        };
    }

    /// <summary>
    /// Builds an isolated candidate and never executes its opening. Current scene may differ from
    /// Story.SceneId after travel. Occupancy is intentionally not checked: scripts can block the saved cell.
    /// </summary>
    public static GameSession Restore(SceneDefinition startingDefinition, StoryCatalog? story, SessionSnapshot snapshot,
        WorldMapDefinition? worldMap = null, BattleCatalog? battles = null)
    {
        ArgumentNullException.ThrowIfNull(startingDefinition); ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Version != SessionSnapshot.CurrentVersion || snapshot.SceneId < 0 ||
            string.IsNullOrWhiteSpace(snapshot.DefinitionId) || snapshot.DefinitionId != startingDefinition.Id ||
            !SceneDefinition.Contains(snapshot.Position) || !Enum.IsDefined(snapshot.Facing) || snapshot.Steps < 0 || snapshot.Steps == long.MaxValue ||
            snapshot.TravelToken is < 0 or long.MaxValue || snapshot.WalkFrameOffset is < 0 or > 12 || snapshot.WalkFrameOffset % 2 != 0 ||
            snapshot.ShipFrameOffset is < 0 or > 6 || snapshot.ShipFrameOffset % 2 != 0 ||
            snapshot.PictureOverride is < short.MinValue or > short.MaxValue ||
            snapshot.LastWorldEntryDirection.HasValue && !Enum.IsDefined(snapshot.LastWorldEntryDirection.Value) ||
            snapshot.HpCostScale is < short.MinValue or > short.MaxValue ||
            snapshot.CurrentMusicId is < 0 or > 23 || snapshot.ExitMusicOverride is < short.MinValue or > 23 ||
            !double.IsFinite(snapshot.FadeOpacity) || snapshot.FadeOpacity is < 0 or > 1 ||
            snapshot.Camera is { } camera && (!double.IsFinite(camera.X) || !double.IsFinite(camera.Y) || camera.X is < 0 or > 63 || camera.Y is < 0 or > 63))
            throw new ArgumentException("Invalid session checkpoint fields.", nameof(snapshot));
        if ((story?.Party is null) != (snapshot.Party is null) || (story is null) != (snapshot.Events is null) ||
            snapshot.Inventory is null || snapshot.ItemIds is null || snapshot.Variables is null || snapshot.Scenes is null)
            throw new ArgumentException("Saved state does not match the story services.", nameof(snapshot));
        if (story?.World is { } definition && !definition.HasScene(snapshot.SceneId) ||
            story is not null && story.World is null && snapshot.SceneId != story.SceneId)
            throw new ArgumentException("Saved scene is unavailable.", nameof(snapshot));
        if (story?.World is { } worldDefinition && worldDefinition.Scene(snapshot.SceneId).ToSceneDefinition().Id != startingDefinition.Id)
            throw new ArgumentException("Starting definition belongs to another scene.", nameof(startingDefinition));
        bool KnownItem(int id) => id >= 0 && story?.ItemNames.ContainsKey(id) == true;
        if (snapshot.ItemIds.Distinct().Count() != snapshot.ItemIds.Length || snapshot.ItemIds.Any(id => !KnownItem(id)) ||
            snapshot.Inventory.Any(pair => !KnownItem(pair.Key) || pair.Value <= 0 || !snapshot.ItemIds.Contains(pair.Key)) ||
            snapshot.Variables.Keys.Any(name => name is null || story?.VariableNames.Values.Contains(name, StringComparer.Ordinal) != true))
            throw new ArgumentException("Invalid saved inventory or named variables.", nameof(snapshot));
        if (snapshot.Party is { } party)
        {
            if (party.ItemUsers is null || party.ItemUsers.Keys.Any(id => !KnownItem(id)))
                throw new ArgumentException("Invalid saved item ownership.", nameof(snapshot));
            if (party.Roles is null) throw new ArgumentException("Missing saved role records.", nameof(snapshot));
            foreach (var words in party.Roles.Values)
            {
                if (words is null || words.Length != RoleWords.Count) throw new ArgumentException("Invalid saved role record.", nameof(snapshot));
                foreach (var word in new[] { RoleWords.EquipmentBegin, RoleWords.EquipmentBegin + 1, RoleWords.PracticeItem }
                    .Concat(Enumerable.Range(RoleWords.TakingItemBegin, RoleWords.TakingItemCount)))
                    if (words[word] != -1 && !KnownItem(words[word])) throw new ArgumentException("Saved role carries an unknown item.", nameof(snapshot));
            }
        }
        if (snapshot.IsWorld && (worldMap is null || snapshot.Travel is null) || snapshot.Travel is not null && worldMap is null)
            throw new ArgumentException("Saved world travel has no world map.", nameof(snapshot));
        if (snapshot.Travel is { } travel)
        {
            WorldTravelRules.Validate(travel);
            // Ship bow is a one-cell lookahead and may legitimately sit just outside the map.
            if (!WorldMapDefinition.Contains(travel.ShipPosition) || travel.ShipBow.X is < -1 or > 512 || travel.ShipBow.Y is < -1 or > 512 ||
                travel.InShip && travel.Position != travel.ShipPosition)
                throw new ArgumentException("Invalid saved ship coordinates.", nameof(snapshot));
        }
        var candidate = new GameSession(startingDefinition, story, worldMap: worldMap, travelStart: snapshot.Travel,
            battles: battles, startInitialProgram: false, sceneIdOverride: snapshot.SceneId, validateSpawn: false);
        candidate.Party?.RestoreState(snapshot.Party!);
        candidate.World.RestoreState(snapshot.Scenes, snapshot.SceneId);
        candidate.Scene = candidate.World.Scene();
        candidate._position = snapshot.Position; candidate._facing = snapshot.Facing; candidate._steps = snapshot.Steps;
        candidate._picture = snapshot.PictureOverride; candidate._walkFrameOffset = snapshot.WalkFrameOffset;
        candidate._shipFrameOffset = snapshot.ShipFrameOffset; candidate.IsWorld = snapshot.IsWorld;
        candidate._lastWorldEntryDirection = snapshot.LastWorldEntryDirection; candidate._travelToken = snapshot.TravelToken;
        candidate._battleHpCostScale = snapshot.HpCostScale; candidate.Camera = snapshot.Camera; candidate.FadeOpacity = snapshot.FadeOpacity;
        candidate._inventory.Clear(); foreach (var pair in snapshot.Inventory) candidate._inventory.Add(pair.Key, pair.Value);
        candidate._itemIds.Clear(); candidate._itemIds.UnionWith(snapshot.ItemIds);
        candidate._variables.Clear(); foreach (var pair in snapshot.Variables) candidate._variables.Add(pair.Key, pair.Value);
        candidate.Events?.RestoreIdleState(snapshot.Events!);
        candidate.RestoreShopStocks(snapshot.ShopStocks);
        candidate._exitMusicOverride = snapshot.ExitMusicOverride;
        if (snapshot.CurrentMusicId is { } music) candidate.RequestMusic(music, force: true);
        return candidate;
    }
}
