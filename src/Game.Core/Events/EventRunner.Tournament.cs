namespace JinYong.Core.Events;

public sealed partial class EventRunner
{
    private enum TournamentPhase
    {
        Choose, BeforeBattleRender, StartBattle, AwaitBattle, VictoryRender, VictoryFade, VictoryTalk,
        VictoryRefresh, AfterVictory, BetweenTalk, BetweenRefresh, BetweenFadeOut, BetweenWait,
        BetweenRestore, BetweenTalkAfter, BetweenRefreshAfter, NextGroup, FinaleTalk, FinaleRefresh,
        FinaleFadeOut, FinaleHide, FinaleFadeIn, FinaleTalkAfter, FinaleRefreshAfter, Reward, RewardRefresh, Complete
    }

    private sealed class TournamentState(EventCommand origin)
    {
        public EventCommand Origin { get; } = origin;
        public bool[] Chosen { get; } = new bool[30];
        public int Group, Victories, Cell, FinaleIndex;
        public TournamentPhase Phase;
    }

    private static readonly int[] TournamentHeads =
    [8,21,23,31,32,43,7,11,14,20,33,34,10,12,19,22,56,68,13,55,62,67,70,71,26,57,60,64,3,69];
    private static readonly (int Talk, int Head, int Style)[] TournamentFinale =
    [(2884,0,1), (2885,70,0), (2886,12,0), (2887,64,4), (2888,19,0)];
    private TournamentState? _tournament;

    // A finite continuation for opcode58, inside this runner. It does not
    // interpret another command language or ask the host to choose battle results.
    private bool AdvanceTournament()
    {
        for (var budget = 0; _tournament is { } trial && budget < 128; budget++)
        {
            switch (trial.Phase)
            {
                case TournamentPhase.Choose:
                    var accepted = false;
                    for (var draw = 0; draw < 4096; draw++)
                    {
                        var cell = trial.Group * 6 + RandomBounded(6);
                        if (trial.Chosen[cell]) continue;
                        trial.Chosen[cell] = true; trial.Cell = cell; accepted = true; break;
                    }
                    if (!accepted) throw new InvalidOperationException("Tournament opponent selection exceeded its safety budget.");
                    trial.Phase = TournamentPhase.BeforeBattleRender;
                    StartDialogue(2854 + trial.Cell, TournamentHeads[trial.Cell], 0); return true;
                case TournamentPhase.BeforeBattleRender:
                    trial.Phase = TournamentPhase.StartBattle; _refresh = true; return true;
                case TournamentPhase.StartBattle:
                    _world.BeginBattle(102 + trial.Cell, 0);
                    trial.Phase = TournamentPhase.AwaitBattle;
                    _battleCommand = null; PendingBattle = new(++_token, 102 + trial.Cell, 0); return true;
                case TournamentPhase.AwaitBattle:
                    return true;
                case TournamentPhase.VictoryRender:
                    trial.Phase = TournamentPhase.VictoryFade; _refresh = true; return true;
                case TournamentPhase.VictoryFade:
                    trial.Phase = TournamentPhase.VictoryTalk; BeginTournamentFade(0, trial.Origin); return true;
                case TournamentPhase.VictoryTalk:
                    trial.Phase = TournamentPhase.VictoryRefresh; StartDialogue(2890, 0, 1); return true;
                case TournamentPhase.VictoryRefresh:
                    trial.Phase = TournamentPhase.AfterVictory; _refresh = true; return true;
                case TournamentPhase.AfterVictory:
                    trial.Victories++;
                    trial.Phase = trial.Victories < 3 ? TournamentPhase.Choose :
                        trial.Group < 4 ? TournamentPhase.BetweenTalk : TournamentPhase.FinaleTalk;
                    break;
                case TournamentPhase.BetweenTalk:
                    trial.Phase = TournamentPhase.BetweenRefresh; StartDialogue(2891, 70, 0); return true;
                case TournamentPhase.BetweenRefresh:
                    trial.Phase = TournamentPhase.BetweenFadeOut; _refresh = true; return true;
                case TournamentPhase.BetweenFadeOut:
                    trial.Phase = TournamentPhase.BetweenWait; BeginTournamentFade(1, trial.Origin); return true;
                case TournamentPhase.BetweenWait:
                    trial.Phase = TournamentPhase.BetweenRestore;
                    _wait = TimeSpan.FromSeconds(8.0 * 65536 / 1193182); return true;
                case TournamentPhase.BetweenRestore:
                    _world.RestTournamentProtagonist();
                    trial.Phase = TournamentPhase.BetweenTalkAfter; BeginTournamentFade(0, trial.Origin); return true;
                case TournamentPhase.BetweenTalkAfter:
                    trial.Phase = TournamentPhase.BetweenRefreshAfter; StartDialogue(2892, 0, 1); return true;
                case TournamentPhase.BetweenRefreshAfter:
                    trial.Phase = TournamentPhase.NextGroup; _refresh = true; return true;
                case TournamentPhase.NextGroup:
                    trial.Group++; trial.Victories = 0; trial.Phase = TournamentPhase.Choose; break;
                case TournamentPhase.FinaleTalk:
                    var line = TournamentFinale[trial.FinaleIndex];
                    trial.Phase = TournamentPhase.FinaleRefresh; StartDialogue(line.Talk, line.Head, line.Style); return true;
                case TournamentPhase.FinaleRefresh:
                    trial.FinaleIndex++;
                    trial.Phase = trial.FinaleIndex < TournamentFinale.Length ? TournamentPhase.FinaleTalk : TournamentPhase.FinaleFadeOut;
                    _refresh = true; return true;
                case TournamentPhase.FinaleFadeOut:
                    trial.Phase = TournamentPhase.FinaleHide; BeginTournamentFade(1, trial.Origin); return true;
                case TournamentPhase.FinaleHide:
                    for (var eventIndex = 24; eventIndex <= 72; eventIndex++)
                        _world.ChangeEvent(_context, _trigger,
                            [-2, eventIndex, 0, 0, -1, -1, -1, -1, -1, -1, -2, -2, -2]);
                    trial.Phase = TournamentPhase.FinaleFadeIn; _refresh = true; return true;
                case TournamentPhase.FinaleFadeIn:
                    trial.Phase = TournamentPhase.FinaleTalkAfter; BeginTournamentFade(0, trial.Origin); return true;
                case TournamentPhase.FinaleTalkAfter:
                    trial.Phase = TournamentPhase.FinaleRefreshAfter; StartDialogue(2889, 0, 1); return true;
                case TournamentPhase.FinaleRefreshAfter:
                    trial.Phase = TournamentPhase.Reward; _refresh = true; return true;
                case TournamentPhase.Reward:
                    _world.AddItem(143, 1);
                    trial.Phase = TournamentPhase.RewardRefresh;
                    Pending = new(++_token, EventPresentationKind.Item, -1, -1, 1,
                        $"获得 {_catalog.ItemNames[143]} × 1", 1, 1); return true;
                case TournamentPhase.RewardRefresh:
                    trial.Phase = TournamentPhase.Complete; _refresh = true; return true;
                case TournamentPhase.Complete:
                    _tournament = null; return false;
                default: throw new InvalidOperationException("Unknown tournament continuation phase.");
            }
        }
        if (_tournament is not null) throw new InvalidOperationException("Tournament continuation exceeded its transition budget.");
        return false;
    }

    private void BeginTournamentFade(int target, EventCommand origin)
    {
        _fadeTarget = target; _fadeDuration = TimeSpan.FromMilliseconds(450);
        _fadeElapsed = TimeSpan.Zero; _fadeCommand = origin;
        _world.SetFade(1 - target); _fading = true;
    }
}
