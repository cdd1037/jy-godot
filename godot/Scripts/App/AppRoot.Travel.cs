using Godot;

namespace JinYong.Host;

public partial class AppRoot
{
    private void CompletePendingTravel()
    {
        if (_busy || _world.Session is not { IsTravelReady: true, PendingTravel: { } request } session) return;
        try
        {
            var library = CurrentPackage?.Library ?? throw new InvalidOperationException("Travel requires a complete resource library.");
            var view = request.IsWorld ? library.OpenWorldView(session)
                : library.OpenScene(request.SceneId, request.Position, validateSpawn: false) with { Story = session.Story };
            _viewHost.Load(view, session, request.Token);
            if (!session.IsWorld) _sceneNumber.Value = session.World.CurrentSceneId;
            _heldDirections.Clear();
            GetViewport().GuiGetFocusOwner()?.ReleaseFocus();
            GD.Print($"TRAVEL_COMMITTED world={session.IsWorld} scene={session.World.CurrentSceneId} position={session.Player.Position}");
        }
        catch (Exception error)
        {
            session.RejectTravel(request.Token, "无法进入目的地：" + error.Message);
            GD.PushError(error.ToString());
            if (_smoke) _ = FinishSmokeAsync(false, error.Message);
        }
    }
}
