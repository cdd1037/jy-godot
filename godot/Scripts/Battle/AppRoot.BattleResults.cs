using Godot;
using JinYong.Core.Battles;
using JinYong.Core.Session;

namespace JinYong.Host;

public partial class AppRoot
{
    private Control _battleResultsPanel = null!;
    private Label _battleResultsTitle = null!, _battleResultsSummary = null!, _battleResultsError = null!;
    private VBoxContainer _battleResultsEntries = null!;
    private Button _battleResultsContinue = null!;
    private BattleSession? _battleResultsBattle, _battleResultsAutoAttempt;
    private GameSession? _battleResultsSession;
    private bool _battleResultsConfirming;
    private int _battleResultsConfirmations;
    private bool BattleResultsVisible => _battleResultsPanel?.Visible == true;

    private void BuildBattleResultsUi()
    {
        _battleResultsPanel = new Control { Name = "BattleResults", Visible = false, MouseFilter = MouseFilterEnum.Stop, ZIndex = 100 };
        _battleResultsPanel.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(_battleResultsPanel);
        var veil = new ColorRect { Color = new Color(0, 0, 0, 0.88f), MouseFilter = MouseFilterEnum.Stop };
        veil.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _battleResultsPanel.AddChild(veil);
        var center = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        center.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect); _battleResultsPanel.AddChild(center);
        var card = new PanelContainer { CustomMinimumSize = new Vector2(680, 0) };
        card.AddThemeStyleboxOverride("panel", Box("192720", 12)); center.AddChild(card);
        var column = new VBoxContainer(); column.AddThemeConstantOverride("separation", 12); card.AddChild(column);
        _battleResultsTitle = new Label(); _battleResultsTitle.AddThemeFontSizeOverride("font_size", 28); column.AddChild(_battleResultsTitle);
        _battleResultsSummary = new Label { CustomMinimumSize = new Vector2(620, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(_battleResultsSummary);
        var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(620, 300), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        column.AddChild(scroll);
        _battleResultsEntries = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _battleResultsEntries.AddThemeConstantOverride("separation", 12); scroll.AddChild(_battleResultsEntries);
        _battleResultsError = new Label { CustomMinimumSize = new Vector2(620, 0), AutowrapMode = TextServer.AutowrapMode.WordSmart,
            Modulate = new Color("e6b89d") };
        column.AddChild(_battleResultsError);
        _battleResultsContinue = new Button { Name = "ContinueBattleResults", Text = "继续剧情", CustomMinimumSize = new Vector2(620, 40) };
        _battleResultsContinue.Pressed += ConfirmBattleResults;
        column.AddChild(_battleResultsContinue);
        column.AddChild(new Label { Text = "Enter / 空格继续", Modulate = new Color("94a78f") });
    }

    private void ShowBattleResults(BattleSession battle)
    {
        if (_world.Session is not { } session || !ReferenceEquals(session.Battle, battle) ||
            battle.Phase != BattlePhase.Finished || battle.IsPresenting) return;
        _battleResultsAutoAttempt = battle;
        try
        {
            if (!session.PrepareBattleSettlement() || session.PendingBattleSettlement is not { } result)
            {
                _battleUiError = session.BattleSettlementError ?? "战果尚未就绪，请稍后重试。";
                return;
            }
            if (!ReferenceEquals(_battleResultsBattle, battle) || !ReferenceEquals(_battleResultsSession, session))
            {
                foreach (var child in _battleResultsEntries.GetChildren()) { _battleResultsEntries.RemoveChild(child); child.QueueFree(); }
                var sections = BattleResultsText.Build(session, result);
                foreach (var section in sections)
                {
                    var heading = new Label { Text = section.Title };
                    heading.AddThemeFontSizeOverride("font_size", 20); heading.AddThemeColorOverride("font_color", new Color("e6d2a4"));
                    _battleResultsEntries.AddChild(heading);
                    _battleResultsEntries.AddChild(new Label { Text = string.Join("\n", section.Lines), CustomMinimumSize = new Vector2(600, 0),
                        AutowrapMode = TextServer.AutowrapMode.WordSmart });
                    _battleResultsEntries.AddChild(new HSeparator());
                }
                if (sections.Count == 0)
                    _battleResultsEntries.AddChild(new Label { Text = "此战没有升级、修炼或制作成果。" });
                _battleResultsBattle = battle; _battleResultsSession = session;
            }
            _battleResultsTitle.Text = battle.Outcome == BattleOutcome.Victory ? "战斗胜利" : "战斗落败";
            _battleResultsSummary.Text = battle.Definition.Name +
                (battle.Outcome == BattleOutcome.Victory && battle.Combatants.Any(actor => actor.IsFriendly && !actor.Hidden)
                    ? $"\n存活队员每人分得经验 {result.SharedExperience}。" : "\n休整之后，继续闯荡江湖。");
            _battleResultsError.Text = ""; _battleResultsContinue.Disabled = false;
            SetBattleInputMode(BattleInputMode.None);
            _heldDirections.Clear(); _world.ReadMovement([], false);
            _battleResultsPanel.Show(); _battleResultsContinue.GrabFocus();
        }
        catch (Exception error)
        {
            _battleUiError = "战果显示失败：" + error.Message;
            GD.PushError(error.ToString());
        }
    }

    private void ConfirmBattleResults()
    {
        if (_battleResultsConfirming || !BattleResultsVisible || _battleResultsSession is not { } session ||
            !ReferenceEquals(_world.Session, session) || !ReferenceEquals(session.Battle, _battleResultsBattle)) return;
        _battleResultsConfirming = true; _battleResultsContinue.Disabled = true;
        try
        {
            if (!session.FinishBattle())
            {
                _battleResultsError.Text = "战斗结果尚未就绪，请稍后重试。";
                return;
            }
            _battleResultsConfirmations++;
            CloseBattleResults();
            _heldDirections.Clear(); _modalClosedFrame = Engine.GetProcessFrames();
            AdvanceBattleHost(0); BindStory(); _world.QueueRedraw();
        }
        catch (Exception error)
        {
            _battleResultsError.Text = "无法继续剧情：" + error.Message;
            GD.PushError(error.ToString());
        }
        finally
        {
            _battleResultsConfirming = false;
            _battleResultsContinue.Disabled = false;
        }
    }

    private void CloseBattleResults()
    {
        _battleResultsPanel?.Hide();
        _battleResultsBattle = null; _battleResultsSession = null; _battleResultsAutoAttempt = null;
        GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
    }
}
