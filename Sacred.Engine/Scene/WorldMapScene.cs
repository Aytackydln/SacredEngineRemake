using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene.WorldMap;
using Sacred.World.Map;

namespace Sacred.Engine.Scene;

internal sealed class WorldMapScene : IGameScene
{
    private readonly InputState _input;
    private readonly Win32Window _window;
    private readonly Func<Vector2> _getPlayerWorldPosition;
    private readonly WorldMapCamera _camera = new();
    private readonly WorldMapInputController _inputController;
    private readonly PlaceholderScreenRasterizer _placeholderRasterizer;
    private readonly Task<WorldMapAtlas> _atlasLoad;

    private ScreenFrame _screen;
    private ScreenFrame? _mapFrame;
    private WorldMapAtlas? _atlas;
    private Vector2 _playerMapPosition;
    private int _viewportWidth;
    private int _viewportHeight;
    private bool _loadFailureReported;
    private ulong _mapRevision;

    public WorldMapScene(
        Win32Window window,
        GamepadInputSource gamepad,
        Action<GameSceneId> requestSwitch,
        Action<Vector2> teleport,
        Func<string, CancellationToken, Task<TextureAsset>> loadTextureAsync,
        Func<Vector2> getPlayerWorldPosition,
        string gameDirectory)
    {
        _window = window;
        _input = window.Input;
        _getPlayerWorldPosition = getPlayerWorldPosition;
        _inputController = new WorldMapInputController(_input, gamepad, _camera, teleport, requestSwitch);
        _placeholderRasterizer = new PlaceholderScreenRasterizer(gameDirectory);
        _screen = _placeholderRasterizer.Rasterize("WORLD MAP", "LOADING ANCARIA MAP...");
        _atlasLoad = new WorldMapAtlasLoader(loadTextureAsync).LoadAsync();
    }

    public GameSceneId Id => GameSceneId.WorldMap;
    public void OnActivated()
    {
        _input.ClearTransientEvents();
        _inputController.Reset();
        _window.RequestFocus();
        if (_atlas is not null)
            CenterOnPlayer();
        EngineLog.WriteLine("World map scene activated.");
    }

    public void OnDeactivated() => _inputController.Reset();

    public void Update(float deltaSeconds)
    {
        ApplyLoadedAtlas();
        if (_atlas is null)
        {
            _inputController.Update(
                deltaSeconds,
                2048,
                2048,
                _window.ClientWidth,
                _window.ClientHeight);
            return;
        }

        var viewportWidth = _window.ClientWidth;
        var viewportHeight = _window.ClientHeight;
        if (viewportWidth != _viewportWidth || viewportHeight != _viewportHeight)
        {
            _viewportWidth = viewportWidth;
            _viewportHeight = viewportHeight;
            _camera.Pan(Vector2.Zero, _atlas.Width, _atlas.Height, viewportWidth, viewportHeight);
        }

        _inputController.Update(
            deltaSeconds,
            _atlas.Width,
            _atlas.Height,
            viewportWidth,
            viewportHeight);
    }

    public ValueTask RenderAsync(SceneRenderContext context)
    {
        if (_mapFrame is not null)
        {
            return context.Renderer.RenderWorldMapAsync(
                new WorldMapFrame(
                    _mapFrame,
                    _camera.Center,
                    _camera.Zoom,
                    new WorldMapOverlay(
                        _inputController.TargetWorldPosition,
                        _inputController.TargetScreenPosition,
                        _inputController.IsControllerTargetVisible,
                        _inputController.IsMinimapVisible,
                        "Silver",
                        string.Empty)),
                context.VerticalSyncEnabled,
                context.FrameId,
                context.CancellationToken);
        }

        return context.Renderer.RenderScreenFrameAsync(
            _screen,
            context.VerticalSyncEnabled,
            context.FrameId,
            context.CancellationToken);
    }

    private void ApplyLoadedAtlas()
    {
        if (_atlas is not null || !_atlasLoad.IsCompleted)
            return;

        if (!_atlasLoad.IsCompletedSuccessfully)
        {
            if (!_loadFailureReported)
            {
                _loadFailureReported = true;
                EngineLog.WriteLine($"World map failed to load: {_atlasLoad.Exception}");
                _screen = _placeholderRasterizer.Rasterize("WORLD MAP", "MAP ASSETS COULD NOT BE LOADED");
            }
            return;
        }

        _atlas = _atlasLoad.Result;
        CenterOnPlayer();
    }

    private void CenterOnPlayer()
    {
        if (_atlas is null)
            return;

        _viewportWidth = _window.ClientWidth;
        _viewportHeight = _window.ClientHeight;
        _playerMapPosition = WorldMapProjection.WorldToMap(_getPlayerWorldPosition(), _atlas.Width);
        _camera.CenterOn(
            _playerMapPosition,
            _atlas.Width,
            _atlas.Height,
            _viewportWidth,
            _viewportHeight);
        _mapFrame = WorldMapFrameBuilder.Create(_atlas, _playerMapPosition, ++_mapRevision);
        EngineLog.WriteLine(
            $"World map centered on player at map pixel {_playerMapPosition.X:0.0},{_playerMapPosition.Y:0.0}.");
    }

    public void Dispose()
    {
        try
        {
            _atlasLoad.GetAwaiter().GetResult();
        }
        catch
        {
            // Loading failures are surfaced while the scene is active.
        }
        _placeholderRasterizer.Dispose();
    }
}
