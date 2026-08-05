using System;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics;
using Sacred.Engine.Platform;
using Sacred.Engine.World;

namespace Sacred.Engine.Scene.InGame;

internal sealed class InGameScene : IGameScene
{
    private readonly AssetManager _assets;
    private readonly WorldStreamer _worldStreamer;
    private readonly SacredCamera _camera;
    private readonly SceneState _scene = new();
    private readonly PlayerCharacterController _player;
    private readonly WorldLightingController _worldLighting;
    private readonly InGameInputController _inputController;
    private readonly Win32Window _window;
    private bool _disposed;

    public InGameScene(
        LoadedGameResources resources,
        Dx12Renderer renderer,
        Win32Window window,
        GamepadInputSource gamepad,
        Action<GameSceneId> requestSwitch,
        Action updateWindowTitle)
    {
        _assets = resources.Assets;
        Renderer = renderer;
        _window = window;
        _worldStreamer = new WorldStreamer(resources.WorldArchive);
        _camera = SacredCamera.CreateDefault(window.ClientWidth, window.ClientHeight);
        _player = new PlayerCharacterController(_assets, _scene);
        _worldLighting = new WorldLightingController();
        _inputController = new InGameInputController(
            window.Input,
            gamepad,
            _camera,
            new ClickToMoveController(),
            _player,
            _worldStreamer,
            _scene,
            _worldLighting,
            requestSwitch,
            updateWindowTitle,
            () => window.ClientWidth,
            () => window.ClientHeight);
        Bootstrap();
    }

    public GameSceneId Id => GameSceneId.InGame;
    public Dx12Renderer Renderer { get; }
    public string LightingDisplayName => _worldLighting.DisplayName;

    public void OnActivated()
    {
        _window.Input.ClearTransientEvents();
        _window.RequestFocus();
    }
    public void OnDeactivated() { }
    public void Update(float deltaSeconds) => _inputController.Update(deltaSeconds);

    public ValueTask RenderAsync(SceneRenderContext context) =>
        context.Renderer.RenderFrameAsync(
            _camera,
            _worldStreamer.VisibleWorld,
            _scene,
            context.VerticalSyncEnabled,
            context.FramePacingStatus,
            context.FrameId,
            context.CancellationToken);

    public WorldPreloadRequest CreatePreloadRequest() =>
        new(_camera, _worldStreamer.VisibleWorld, _scene);

    private void Bootstrap()
    {
        _worldStreamer.CenterOnSector(_worldStreamer.StartSector.X, _worldStreamer.StartSector.Y);
        _camera.CenterOnTile(
            _worldStreamer.StartSector.X * WorldStreamer.SectorTileCount + WorldStreamer.SectorTileCount * 0.5f,
            _worldStreamer.StartSector.Y * WorldStreamer.SectorTileCount + WorldStreamer.SectorTileCount * 0.5f,
            0.75f);
        _scene.Lighting.LightPosition = _camera.EyePosition + new Vector3(-320.0f, -180.0f, 260.0f);
        _player.Initialize(_camera.WorldCenter);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _player.Dispose();
        _worldStreamer.Dispose();
        _assets.Dispose();
    }
}
