using System;
using System.IO;
using System.Threading.Tasks;
using Sacred.Engine.Assets;
using Sacred.Engine.Rendering;

namespace Sacred.Engine.Scene;

internal sealed class InitialLoadingScene : IGameScene
{
    private readonly ResourceLoadSequence _loads;
    private readonly LoadingScreenRasterizer _rasterizer;
    private readonly Action _onLoaded;
    private ScreenFrame _screen;
    private bool _completionRaised;

    public InitialLoadingScene(
        GameResourceLoader resources,
        string gameDirectory,
        Action onLoaded)
    {
        _loads = new ResourceLoadSequence(resources.CreateInitialLoadSteps());
        _rasterizer = new LoadingScreenRasterizer(
            Path.Combine(resources.PakDirectory, "LoadingUW00.bmp"),
            Path.Combine(resources.PakDirectory, "loadgame.bmp"),
            gameDirectory);
        _onLoaded = onLoaded ?? throw new ArgumentNullException(nameof(onLoaded));
        _screen = _rasterizer.Rasterize(0.0, _loads.CurrentItem);
    }

    public GameSceneId Id => GameSceneId.InitialLoading;

    public void OnActivated() { }
    public void OnDeactivated() { }

    public void Update(float deltaSeconds)
    {
        if (_completionRaised || !_loads.Update())
            return;

        _screen = _rasterizer.Rasterize(_loads.Progress, _loads.CurrentItem);
        if (!_loads.IsComplete)
            return;

        _completionRaised = true;
        _onLoaded();
    }

    public ValueTask RenderAsync(SceneRenderContext context) =>
        context.Renderer.RenderScreenFrameAsync(
            _screen,
            context.VerticalSyncEnabled,
            context.FrameId,
            context.CancellationToken);

    public void Dispose()
    {
        _loads.WaitForActiveStep();
        _rasterizer.Dispose();
    }
}
