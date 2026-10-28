using JinYong.Core.Maps;
using JinYong.Core.Session;

namespace JinYong.Core.Persistence;

/// <summary>Exploration checkpoint only. Definitions/assets and active continuations are not serialized.</summary>
public sealed record SessionSnapshot
{
    public const int CurrentVersion = 1;
    public int Version { get; init; } = CurrentVersion;
    public int SceneId { get; init; }
    public string DefinitionId { get; init; } = "";
    public bool IsWorld { get; init; }
    public GridPosition Position { get; init; }
    public Direction Facing { get; init; }
    public long Steps { get; init; }
    public int? PictureOverride { get; init; }
    public int WalkFrameOffset { get; init; }
    public int ShipFrameOffset { get; init; }
    public WorldTravelSnapshot? Travel { get; init; }
    public Direction? LastWorldEntryDirection { get; init; }
    public long TravelToken { get; init; }
    public int HpCostScale { get; init; }
    public CameraView? Camera { get; init; }
    public double FadeOpacity { get; init; }
    public PartySnapshot? Party { get; init; }
    public Dictionary<int, int> Inventory { get; init; } = [];
    public int[] ItemIds { get; init; } = [];
    public Dictionary<string, int> Variables { get; init; } = new(StringComparer.Ordinal);
    public SceneStateSnapshot[] Scenes { get; init; } = [];
    public EventRunnerIdleSnapshot? Events { get; init; }
}

public sealed record PartySnapshot(int[] Members, Dictionary<int, int[]> Roles, Dictionary<int, int> ItemUsers);
public sealed record SceneStateSnapshot(int SceneId, short[] Layers, short[][] Events, short[] Metadata, long Revision);
public sealed record EventRunnerIdleSnapshot(uint RandomState, long Token, IReadOnlyList<int> CompletedPrograms);
