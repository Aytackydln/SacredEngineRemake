using System.Threading.Tasks;
using Sacred.Engine.Rendering;

namespace Sacred.Engine.Scene;

internal sealed class PlaceholderScene : IGameScene
{
    private readonly PlaceholderScreenRasterizer _rasterizer;
    private readonly ScreenFrame _screen;

    public PlaceholderScene(GameSceneId id, string title, string message, string gameDirectory)
    {
        Id = id;
        _rasterizer = new PlaceholderScreenRasterizer(gameDirectory);
        _screen = _rasterizer.Rasterize(title, message);
    }

    public GameSceneId Id { get; }
    public void OnActivated() { }
    public void OnDeactivated() { }
    public void Update(float deltaSeconds) { }

    public ValueTask RenderAsync(SceneRenderContext context) =>
        context.Renderer.RenderScreenFrameAsync(
            _screen,
            context.VerticalSyncEnabled,
            context.FrameId,
            context.CancellationToken);

    public void Dispose() => _rasterizer.Dispose();
}
