using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics;
using Sacred.Engine.Platform;
using Sacred.World;

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
    private readonly SacredGameSaveState _saveState;
    private Task? _worldPreparationTask;
    private bool _disposed;

    public InGameScene(
        LoadedGameResources resources,
        Dx12Renderer renderer,
        Win32Window window,
        GamepadInputSource gamepad,
        Action<GameSceneId> requestSwitch,
        Action updateWindowTitle,
        SacredGameSaveState saveState)
    {
        _assets = resources.Assets;
        Renderer = renderer;
        _window = window;
        _saveState = saveState;
        _worldStreamer = new WorldStreamer(resources.WorldArchive);
        _camera = SacredCamera.CreateDefault(window.ClientWidth, window.ClientHeight);
        _player = new PlayerCharacterController(_assets, _scene, saveState.CharacterName);
        _worldLighting = new WorldLightingController(saveState.WorldLightingMode);
        _scene.Debug.StairsMapVisible = saveState.StairsTilesVisible;
        _scene.Debug.BlockedAreasVisible = saveState.BlockedTilesVisible;
        _scene.Minimap.DifficultyDisplayName = "Silver";
        _inputController = new InGameInputController(
            window.Input,
            gamepad,
            _camera,
            new ClickToMoveController(),
            _player,
            new StairsTraversalController(resources.WorldArchive.StairsMap),
            _worldStreamer,
            _scene,
            _worldLighting,
            requestSwitch,
            updateWindowTitle,
            () => window.ClientWidth,
            () => window.ClientHeight,
            window.SetHandCursor);
        Bootstrap();
    }

    public GameSceneId Id => GameSceneId.InGame;
    public Dx12Renderer Renderer { get; }
    public string LightingDisplayName => _worldLighting.DisplayName;
    internal string SelectedCharacterName => _player.SelectedCharacterName;
    internal bool StairsTilesVisible => _scene.Debug.StairsMapVisible;
    internal bool BlockedTilesVisible => _scene.Debug.BlockedAreasVisible;
    internal WorldLightingMode WorldLightingMode => _worldLighting.Mode;
    internal Vector2 PlayerWorldPosition => _camera.WorldCenter;

    internal Task<TextureAsset> LoadTextureAsync(string textureName, CancellationToken cancellationToken) =>
        _assets.LoadTextureAsync(textureName, cancellationToken);

    internal void Teleport(Vector2 destination) => _inputController.Teleport(destination);

    internal bool TrySetCheatOption(string option, string value, out string message)
    {
        switch (option.ToLowerInvariant())
        {
            case "lighting" when TryParseLightingMode(value, out var lightingMode):
                _worldLighting.SetMode(lightingMode);
                message = $"world lighting set to {_worldLighting.DisplayName}";
                return true;
            case "stairs" or "stairs-tiles" when TryParseBoolean(value, out var gateTilesVisible):
                _scene.Debug.StairsMapVisible = gateTilesVisible;
                message = $"stairs tiles {(gateTilesVisible ? "visible" : "hidden")}";
                return true;
            case "blocked" or "blocked-tiles" when TryParseBoolean(value, out var blockedTilesVisible):
                _scene.Debug.BlockedAreasVisible = blockedTilesVisible;
                message = $"blocked tiles {(blockedTilesVisible ? "visible" : "hidden")}";
                return true;
            case "character" when value.Equals("next", StringComparison.OrdinalIgnoreCase):
                _player.CycleModel();
                message = "loading next character";
                return true;
            default:
                message = "Unknown in-game option. Use lighting <day|night|cycle|black>, stairs <on|off>, blocked <on|off>, or character next.";
                return false;
        }
    }

    public void OnActivated()
    {
        _window.Input.ClearTransientEvents();
        _inputController.OnActivated();
        _window.RequestFocus();
    }
    public void OnDeactivated()
    {
        _inputController.OnDeactivated();
        _window.SetHandCursor(false);
    }
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

    /// <summary>Starts sector streaming and the loading-screen-driven GPU upload pipeline.</summary>
    public Task StartWorldPreparation() =>
        _worldPreparationTask ??= Renderer.StartWorldPreparation();

    private void Bootstrap()
    {
        var startLocation = _saveState.LastLocation ?? new Vector2(
            _worldStreamer.StartSector.X * WorldStreamer.SectorTileCount + WorldStreamer.SectorTileCount * 0.5f,
            _worldStreamer.StartSector.Y * WorldStreamer.SectorTileCount + WorldStreamer.SectorTileCount * 0.5f);
        _worldStreamer.CenterOnSector(
            (int)MathF.Floor(startLocation.X / WorldStreamer.SectorTileCount),
            (int)MathF.Floor(startLocation.Y / WorldStreamer.SectorTileCount));
        _camera.CenterOnTile(startLocation.X, startLocation.Y, 0.75f);
        _worldLighting.Update(0.0f, _scene.Lighting, new Vector3(_camera.WorldCenter, 0.0f));
        _player.Initialize(_camera.WorldCenter);
    }

    private static bool TryParseLightingMode(string value, out WorldLightingMode mode)
    {
        mode = value.ToLowerInvariant() switch
        {
            "day" => WorldLightingMode.Day,
            "night" => WorldLightingMode.Night,
            "cycle" or "timed" => WorldLightingMode.TimedDayNightCycle,
            "black" or "pitchblack" => WorldLightingMode.PitchBlack,
            _ => default
        };
        return value.Equals("day", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("night", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("cycle", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("timed", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("black", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("pitchblack", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseBoolean(string value, out bool enabled)
    {
        enabled = value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                  value.Equals("true", StringComparison.OrdinalIgnoreCase);
        return enabled || value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _window.SetHandCursor(false);
        _player.Dispose();
        _worldStreamer.Dispose();
        _assets.Dispose();
    }
}
