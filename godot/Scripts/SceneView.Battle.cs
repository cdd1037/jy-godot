using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Maps;

namespace JinYong.Host;

public partial class SceneView
{
    private GridPosition? _selectedBattleCell;
    private bool _battleMovePreview;
    private Color[] _battlePalette = [];
    private readonly HashSet<string> _missingBattleSpriteMessages = [];
    private readonly Dictionary<Texture2D,ImageTexture> _battleFlashTextures = [];
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
        if(battle.Presentation is { } frame && frame.Combatants.FirstOrDefault(actor=>actor.Slot==frame.ActorSlot) is { } animated)
        {
            var anchor=BattleProject(animated.Position);
            if(frame.Movement is { } move)
                anchor=BattleProject(move.From).Lerp(BattleProject(move.To),SmoothMovement ? (float)move.Progress : 1);
            return Size/2-anchor*_zoom;
        }
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
        var presentation=battle.Presentation;
        var visible=presentation?.Combatants ?? battle.CaptureCombatantSnapshots();
        var occupants=new Dictionary<GridPosition,BattleCombatantSnapshot>();
        Vector2 ActorAnchor(BattleCombatantSnapshot actor)
        {
            if(presentation?.ActorSlot==actor.Slot && presentation.Movement is { } movement)
                return BattleProject(movement.From).Lerp(BattleProject(movement.To),SmoothMovement ? (float)movement.Progress : 1);
            return BattleProject(actor.Position);
        }
        foreach(var actor in visible.Where(actor=>!actor.Hidden))
        {
            var cell=actor.Position;
            if(presentation?.ActorSlot==actor.Slot && presentation.Movement is { } movement)
                cell=(SmoothMovement ? movement.Progress : 1)<0.5 ? movement.From : movement.To;
            occupants[cell]=actor; // Original duplicate coordinates use the last slot.
        }
        void Sprite(int id,Vector2 anchor,string bank="battlefield",Color? flash=null)
        {
            Frame? frame;
            if(bank=="battlefield")
            { if(!_frames.TryGetValue(id,out frame)) return; }
            else if(!_battleFrames.TryGetValue(bank,out var entries) || !entries.TryGetValue(id,out frame))
            {
                if(_missingBattleSpriteMessages.Add($"{bank}:{id}")) GD.PushError($"Missing battle animation sprite {bank}:{id}.");
                return; // Never use another head's same-numbered FIGHT frame.
            }
            if(frame is null) return; // A declared empty frame draws nothing only at this cell.
            var rectangle = new Rect2(origin+(anchor-frame.Offset)*_zoom,frame.Region.Size*_zoom);
            if (!rectangle.Intersects(viewport)) return;
            DrawTextureRectRegion(flash is null ? frame.Texture : BattleFlashTexture(frame.Texture),rectangle,frame.Region,flash);
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
        IReadOnlyDictionary<GridPosition,int>? reachable = presentation is null && BattleMovePreview && battle.CurrentActor?.IsFriendly == true
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
            occupants.TryGetValue(cell,out var occupant);
            if (occupant is not null)
            {
                var active = occupant.Slot==(presentation?.ActorSlot ?? battle.CurrentActor?.Slot);
                var tint = active ? new Color(0.95f,0.8f,0.3f,0.85f) : occupant.IsFriendly
                    ? new Color(0.4f,0.8f,0.6f,0.65f) : new Color(0.9f,0.38f,0.32f,0.65f);
                Diamond(cell,new Color(tint.R,tint.G,tint.B,0.12f),tint);
            }
            if (SelectedBattleCell == cell)
                Diamond(cell,new Color(1,0.85f,0.4f,0.15f),new Color(1,0.9f,0.55f,0.95f));
            var building = battle.Battlefield.At(BattlefieldLayer.Building,cell);
            if (building is not 0 and not 15000) Sprite(building,anchor);
            if(occupant is not null)
            {
                Color? flash=null;
                // Native effect cells own highlighting independently of number
                // entries: a zero-damage throw can flash without drawing "-0".
                // Keep knocked-out snapshots visible, but do not flash them.
                if(presentation?.Flash==true && presentation.HighlightedCells.Contains(cell) && occupant.Hp>=0)
                    flash=BattlePaletteColor(presentation.FlashKind switch
                    {
                        BattleDamageKind.Poison=>0x2f,
                        BattleDamageKind.Mp=>0x4e,
                        _=>0xff
                    });
                if(presentation?.ActorSlot==occupant.Slot && presentation.ActorSprite is { } actorSprite)
                    Sprite(actorSprite.Frame,ActorAnchor(occupant),actorSprite.Bank,flash);
                else Sprite(occupant.SpriteId,ActorAnchor(occupant),flash:flash);
            }
            if(presentation is not null)
            {
                foreach(var effect in presentation.Effects.Where(effect=>effect.Position==cell))
                    Sprite(effect.Frame,anchor,effect.Bank);
                foreach(var damage in presentation.Damage.Where(damage=>damage.Position==cell))
                    DrawBattleDamage(damage,origin+anchor*_zoom,presentation.DamagePhase);
            }
        }
        if(presentation?.ItemUse is { } itemUse)
            DrawBattleItemEffect(itemUse,presentation.Combatants.FirstOrDefault(actor=>actor.Slot==presentation.ActorSlot)?.Name ?? "",presentation.WaitingForConfirmation);
        if (_session.FadeOpacity > 0)
            DrawRect(viewport,new Color(0,0,0,(float)_session.FadeOpacity));
    }

    private bool TryHandleBattleClick(InputEvent input)
    {
        if (_session?.Battle is not { } battle || input is not InputEventMouseButton
            { Pressed:true,ButtonIndex:MouseButton.Left } mouse) return false;
        if(battle.IsPresenting) { AcceptEvent(); return true; }
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

    private Color BattlePaletteColor(int index) => _battlePalette.Length==256 ? _battlePalette[index] : Colors.White;

    // One derived alpha-mask texture per already shared atlas. This reproduces
    // the original opaque-pixel highlight without multiplying sprite RGB, and
    // creates no resource export or per-head copy of the underlying artwork.
    private Texture2D BattleFlashTexture(Texture2D texture)
    {
        if(_battleFlashTextures.TryGetValue(texture,out var existing)) return existing;
        using var source=texture.GetImage();
        source.Convert(Image.Format.Rgba8);
        var pixels=source.GetData();
        for(var index=0;index<pixels.Length;index+=4) pixels[index]=pixels[index+1]=pixels[index+2]=255;
        using var mask=Image.CreateFromData(source.GetWidth(),source.GetHeight(),false,Image.Format.Rgba8,pixels);
        var result=ImageTexture.CreateFromImage(mask); _battleFlashTextures.Add(texture,result); return result;
    }

    private void DrawBattleDamage(BattlePresentationDamage damage,Vector2 anchor,int phase)
    {
        var (foreground,shadow)=damage.Kind switch
        {
            BattleDamageKind.Hp=>(0x10,0x14),BattleDamageKind.Poison=>(0x30,0x32),
            BattleDamageKind.Detox=>(0x91,0x93),BattleDamageKind.Medicine=>(0x07,0x05),_=>(0x50,0x53)
        };
        var sign=damage.Kind is BattleDamageKind.Detox or BattleDamageKind.Medicine ? "+" : "-";
        var text=sign+damage.Amount.ToString().PadLeft(3);
        var size=Math.Max(12,(int)Math.Round(10*_zoom));
        var width=_font.GetStringSize(text,fontSize:size).X;
        var position=anchor+new Vector2(-width/2,-(60+2*phase)*_zoom);
        DrawString(_font,position+new Vector2(1,1)*_zoom,text,fontSize:size,modulate:BattlePaletteColor(shadow));
        DrawString(_font,position,text,fontSize:size,modulate:BattlePaletteColor(foreground));
    }

    private void DrawBattleItemEffect(JinYong.Core.Items.ItemUseResult result,string name,bool waitForConfirmation)
    {
        string[] labels=["生命","最大生命","中毒","体力","内力性质","内力","最大内力","攻击","轻功","防御",
            "医疗","用毒","解毒","抗毒","拳掌","御剑","耍刀","特殊","暗器","武学常识","道德","左右互搏","攻击带毒"];
        var changes=result.Deltas.Select((amount,index)=>(amount,index)).Where(change=>change.amount!=0).ToArray();
        var width=Math.Min(420,Size.X-32); var height=58+24*((changes.Length+1)/2)+(waitForConfirmation ? 28 : 0);
        var panel=new Rect2((Size-new Vector2(width,height))/2,new Vector2(width,height));
        DrawRect(panel,new Color(0.04f,0.08f,0.07f,0.96f));
        DrawRect(panel,new Color("aa9462"),false,1);
        DrawString(_font,panel.Position+new Vector2(16,28),name+" · 物品效果",fontSize:19,modulate:new Color("e6d2a4"));
        for(var index=0;index<changes.Length;index++)
        {
            var (amount,field)=changes[index];
            var position=panel.Position+new Vector2(16+(index%2)*width/2,54+(index/2)*24);
            DrawString(_font,position,$"{labels[field]} {(amount>0 ? "+" : "")}{amount}",fontSize:15,modulate:new Color("ddd7c5"));
        }
        if(waitForConfirmation)
            DrawString(_font,panel.Position+new Vector2(16,height-12),"按任意键继续",fontSize:14,modulate:new Color("a5b8a4"));
    }
}
