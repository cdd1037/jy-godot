namespace JinYong.Core.Events;

public sealed partial class EventRunner
{
    private enum ShopPhase { Greeting, Menu, FeedbackDialogue, FeedbackRender, Close }
    private EventCommand? _shopCommand;
    private ShopPhase _shopPhase;
    private bool _shopPurchased;

    // Z_DAT/0x312A6: greeting, real selection/transaction, feedback, redraw,
    // then hide the auxiliary merchant events. Cancellation has no feedback.
    private bool AdvanceShop()
    {
        switch (_shopPhase)
        {
            case ShopPhase.Greeting:
                _shopPhase = ShopPhase.Menu;
                PendingShop = new(++_token);
                return true;
            case ShopPhase.Menu:
                return true;
            case ShopPhase.FeedbackDialogue:
                _shopPhase = ShopPhase.FeedbackRender;
                StartDialogue(_shopPurchased ? 2976 : 2975, 111, 0);
                return true;
            case ShopPhase.FeedbackRender:
                _shopPhase = ShopPhase.Close; _refresh = true;
                return true;
            case ShopPhase.Close:
                _world.EndShop();
                _shopCommand = null;
                return false;
            default: throw new InvalidOperationException("Unknown shop continuation phase.");
        }
    }
}
