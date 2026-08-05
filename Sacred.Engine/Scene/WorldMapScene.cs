using System;
using System.Threading.Tasks;
using Windows.Gaming.Input;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;

namespace Sacred.Engine.Scene;

internal sealed class WorldMapScene : IGameScene
{
    private readonly InputState _input;
    private readonly GamepadInputSource _gamepad;
    private readonly Action<GameSceneId> _requestSwitch;
    private readonly PlaceholderScreenRasterizer _rasterizer;
    private readonly ScreenFrame _screen;

    public WorldMapScene(
        InputState input,
        GamepadInputSource gamepad,
        Action<GameSceneId> requestSwitch,
        string gameDirectory)
    {
        _input = input;
        _gamepad = gamepad;
        _requestSwitch = requestSwitch;
        _rasterizer = new PlaceholderScreenRasterizer(gameDirectory);
        _screen = _rasterizer.Rasterize("WORLD MAP", "PRESS M, ESC, OR GAMEPAD SELECT TO RETURN");
    }

    public GameSceneId Id => GameSceneId.WorldMap;
    public void OnActivated() => _input.ClearTransientEvents();
    public void OnDeactivated() { }

    public void Update(float deltaSeconds)
    {
        if (_input.ConsumePressed(VirtualKey.M) ||
            _input.ConsumePressed(VirtualKey.Escape) ||
            _gamepad.WasPressed(GamepadButtons.View))
        {
            _requestSwitch(GameSceneId.InGame);
        }
    }

    public ValueTask RenderAsync(SceneRenderContext context) =>
        context.Renderer.RenderScreenFrameAsync(
            _screen,
            context.VerticalSyncEnabled,
            context.FrameId,
            context.CancellationToken);

    public void Dispose() => _rasterizer.Dispose();
}
