using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Maps;

namespace JinYong.Host;

public partial class SceneView
{
    private GridPosition? _selectedBattleCell;
    private bool _battleMovePreview;
    public event Action<GridPosition>? BattleCellSelected;
    public GridPosition? SelectedBattleCell
    {
        get => _selectedBattleCell;
        set { if (_selectedBattleCell == value) return; _selectedBattleCell = value; QueueRedraw(); }
    }
    public bool BattleMovePreview
    {
        get => _battleMovePreview;
        set { if (_battleMovePreview == value) return; _battleMovePreview = value; QueueRedraw(); }
    }

    private static Vector2 BattleProject(GridPosition cell) => new((cell.X-cell.Y)*18,(cell.X+cell.Y)*9);
    private Vector2 BattleOrigin(BattleSession battle)
    {
        var focus = battle.CurrentActor ?? battle.Combatants.FirstOrDefault(actor => actor.IsAlive && actor.IsFriendly)
            ?? battle.Combatants.FirstOrDefault(actor => actor.IsAlive) ?? battle.Combatants.FirstOrDefault();
        return Size/2-BattleProject(focus?.Position ?? new GridPosition(32,32))*_zoom;
    }

    public Vector2 BattleCellScreenPosition(GridPosition cell) => _session?.Battle is { } battle
        ? BattleOrigin(battle) + BattleProject(cell) * _zoom : throw new InvalidOperationException("No battle is active.");

    /// <summary>
    /// Draw the battlefield bank already loaded by the host; no session or asset
    /// export is created here. Reference: OpenLegend BattleSetup::battle_render_plan,
    /// all ground first, then x-major per-cell overlays, building, occupant.
    /// </summary>
    private void DrawBattle()
    {
        if (_session?.Battle is not { } battle) return;
        DrawRect(new Rect2(Vector2.Zero,Size),new Color("101816"));
        DrawnSprites = 0;
        var origin = BattleOrigin(battle);
        var viewport = new Rect2(Vector2.Zero,Size);
        void Sprite(int id,Vector2 anchor)
        {
            if (!_frames.TryGetValue(id,out var frame)) return;
            var rectangle = new Rect2(origin+(anchor-frame.Offset)*_zoom,frame.Region.Size*_zoom);
            if (!rectangle.Intersects(viewport)) return;
            DrawTextureRectRegion(frame.Texture,rectangle,frame.Region);
            DrawnSprites++;
        }
        void Diamond(GridPosition cell,Color fill,Color border)
        {
            var center = origin+BattleProject(cell)*_zoom;
            if (!new Rect2(center-new Vector2(19,10)*_zoom,new Vector2(38,20)*_zoom).Intersects(viewport)) return;
            var points = new Vector2[] {center+new Vector2(0,-9)*_zoom,center+new Vector2(18,0)*_zoom,
                center+new Vector2(0,9)*_zoom,center+new Vector2(-18,0)*_zoom};
            DrawColoredPolygon(points,fill);
            DrawPolyline([.. points,points[0]],border,Math.Max(1,_zoom),true);
        }
        IReadOnlyDictionary<GridPosition,int>? reachable = BattleMovePreview && battle.CurrentActor?.IsFriendly == true
            ? battle.ReachableMoves() : null;
        for (var x=0;x<64;x++)
        for (var y=0;y<64;y++)
        {
            var cell = new GridPosition(x,y);
            Sprite(battle.Battlefield.At(BattlefieldLayer.Ground,cell),BattleProject(cell));
        }
        for (var x=0;x<64;x++)
        for (var y=0;y<64;y++)
        {
            var cell = new GridPosition(x,y);
            var anchor = BattleProject(cell);
            if (reachable?.ContainsKey(cell) == true)
                Diamond(cell,new Color(0.2f,0.65f,0.75f,0.13f),new Color(0.3f,0.75f,0.85f,0.35f));
            var occupant = battle.OccupantAt(cell);
            if (occupant is { IsAlive:true })
            {
                var active = ReferenceEquals(occupant,battle.CurrentActor);
                var tint = active ? new Color(0.95f,0.8f,0.3f,0.85f) : occupant.IsFriendly
                    ? new Color(0.4f,0.8f,0.6f,0.65f) : new Color(0.9f,0.38f,0.32f,0.65f);
                Diamond(cell,new Color(tint.R,tint.G,tint.B,0.12f),tint);
            }
            if (SelectedBattleCell == cell)
                Diamond(cell,new Color(1,0.85f,0.4f,0.15f),new Color(1,0.9f,0.55f,0.95f));
            var building = battle.Battlefield.At(BattlefieldLayer.Building,cell);
            if (building is not 0 and not 15000) Sprite(building,anchor);
            if (occupant is { IsAlive:true }) Sprite(occupant.SpriteId,anchor);
        }
        if (_session.FadeOpacity > 0)
            DrawRect(viewport,new Color(0,0,0,(float)_session.FadeOpacity));
    }

    private bool TryHandleBattleClick(InputEvent input)
    {
        if (_session?.Battle is not { } battle || input is not InputEventMouseButton
            { Pressed:true,ButtonIndex:MouseButton.Left } mouse) return false;
        var point = (mouse.Position-BattleOrigin(battle))/_zoom;
        var x = (point.X/18+point.Y/9)/2;
        var y = (point.Y/9-point.X/18)/2;
        var cell = new GridPosition((int)Math.Round(x,MidpointRounding.AwayFromZero),(int)Math.Round(y,MidpointRounding.AwayFromZero));
        if (BattlefieldDefinition.Contains(cell))
        {
            SelectedBattleCell = cell;
            BattleCellSelected?.Invoke(cell);
        }
        AcceptEvent();
        return true;
    }
}
