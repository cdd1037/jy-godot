using JinYong.Content.Library;
using JinYong.Content.Packages;
using JinYong.Core.Session;

namespace JinYong.Host;

/// <summary>Publishes the package binding only after the view has successfully loaded it.</summary>
public sealed class GameViewHost(SceneView view)
{
    public SceneView View { get; } = view ?? throw new ArgumentNullException(nameof(view));
    public GameSession? Session => View.Session;
    public ScenePackage? Package { get; private set; }
    public GameLibrary? Library => Package?.Library;
    public event Action? SessionChanged;

    public void Load(ScenePackage package, GameSession? retainedSession = null, long? travelToken = null)
    {
        ArgumentNullException.ThrowIfNull(package);
        var oldSession = Session;
        View.LoadPackage(package, retainedSession, travelToken);
        Package = package;
        if (!ReferenceEquals(oldSession, Session)) SessionChanged?.Invoke();
    }
}
