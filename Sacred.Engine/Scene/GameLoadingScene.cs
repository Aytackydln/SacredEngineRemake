using System;
using System.IO;
using System.Threading.Tasks;
using Sacred.Engine.Assets;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene.InGame;

namespace Sacred.Engine.Scene;

internal sealed class GameLoadingScene : IGameScene
{
    private readonly ResourceLoadSequence _loads;
    private readonly LoadingScreenRasterizer _rasterizer;
    private readonly Func<InGameScene> _initializeRuntime;
    private readonly Action<GameSceneId> _requestSwitch;
    private ScreenFrame _screen;
    private InGameScene? _inGame;
    private Task? _worldPreparationTask;
    private string _displayedItem;
    private bool _switchRequested;

    public GameLoadingScene(
        GameResourceLoader resources,
        string gameDirectory,
        Func<InGameScene> initializeRuntime,
        Action<GameSceneId> requestSwitch)
    {
        _loads = new ResourceLoadSequence(resources.CreateGameLoadSteps());
        _rasterizer = new LoadingScreenRasterizer(
            Path.Combine(resources.PakDirectory, "LoadingUW01.bmp"),
            Path.Combine(resources.PakDirectory, "loading0.bmp"),
            gameDirectory);
        _initializeRuntime = initializeRuntime ?? throw new ArgumentNullException(nameof(initializeRuntime));
        _requestSwitch = requestSwitch ?? throw new ArgumentNullException(nameof(requestSwitch));
        _displayedItem = _loads.CurrentItem;
        _screen = _rasterizer.Rasterize(0.0, _displayedItem);
    }

    public GameSceneId Id => GameSceneId.GameLoading;

    public void OnActivated() { }
    public void OnDeactivated() { }

    public void Update(float deltaSeconds)
    {
        if (_switchRequested)
            return;

        if (!_loads.IsComplete)
        {
            if (_loads.Update())
                SetScreen(_loads.Progress * 0.72, _loads.CurrentItem);
            return;
        }

        if (_inGame is null)
        {
            SetScreen(0.78, "Preparing game systems");
            _inGame = _initializeRuntime();
            _worldPreparationTask = _inGame.StartWorldPreparation();
            return;
        }

        var status = _inGame.Renderer.LastWorldPreparationStatus;
        if (_worldPreparationTask is not { IsCompleted: true })
        {
            SetScreen(0.9, status.PendingItem);
            return;
        }

        // The task is deliberately started above, while the loading screen keeps rendering and
        // feeding GPU work. Observe it here so a preparation failure cannot be hidden by status UI.
        _worldPreparationTask.GetAwaiter().GetResult();

        SetScreen(1.0, "World ready");
        _switchRequested = true;
        _requestSwitch(GameSceneId.InGame);
    }

    public ValueTask RenderAsync(SceneRenderContext context)
    {
        var preload = _inGame?.CreatePreloadRequest();
        return context.Renderer.RenderScreenFrameAsync(
            _screen,
            context.VerticalSyncEnabled,
            context.FrameId,
            context.CancellationToken,
            preload);
    }

    private void SetScreen(double progress, string item)
    {
        if (item == _displayedItem && progress < 1.0)
            return;

        _displayedItem = item;
        _screen = _rasterizer.Rasterize(progress, item);
    }

    public void Dispose()
    {
        _loads.WaitForActiveStep();
        _rasterizer.Dispose();
    }
}
