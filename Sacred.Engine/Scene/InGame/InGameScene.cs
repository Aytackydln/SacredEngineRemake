using System;
using System.Globalization;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Platform;
using Sacred.Particles;
using Sacred.World;
using Sacred.World.Particles;

namespace Sacred.Engine.Scene.InGame;

internal sealed class InGameScene : IGameScene
{
    private readonly AssetManager _assets;
    private readonly WorldStreamer _worldStreamer;
    private readonly WorldParticleSystem _particles;
    private readonly SacredCamera _camera;
    private readonly SceneState _scene = new();
    private readonly PlayerCharacterController _player;
    private readonly DoorSceneController _doors;
    private readonly WorldLightingController _worldLighting;
    private readonly InGameInputController _inputController;
    private readonly Win32Window _window;
    private readonly SacredGameSaveState _saveState;
    private Task? _worldPreparationTask;
    private string _lastRegionDisplayName = string.Empty;
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
        _particles = new WorldParticleSystem(resources.WorldArchive.ParticleScript, saveState.ParticleQuality);
        _camera = SacredCamera.CreateDefault(window.ClientWidth, window.ClientHeight);
        _player = new PlayerCharacterController(_assets, _scene, saveState.CharacterName);
        _doors = new DoorSceneController(_assets, _scene);
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
            _doors,
            _worldStreamer,
            _scene,
            _worldLighting,
            requestSwitch,
            updateWindowTitle,
            () => renderer.RenderWidth,
            () => renderer.RenderHeight,
            renderer.OutputToRender,
            window.SetHandCursor);
        Bootstrap();
    }

    public GameSceneId Id => GameSceneId.InGame;
    public Dx12Renderer Renderer { get; }
    public string LightingDisplayName => _worldLighting.DisplayName;
    internal string SelectedCharacterName => _player.SelectedCharacterName;
    internal bool StairsTilesVisible => _scene.Debug.StairsMapVisible;
    internal bool BlockedTilesVisible => _scene.Debug.BlockedAreasVisible;
    internal CollisionCheatMode CollisionMode => _inputController.CollisionMode;
    internal float PlayerMovementSpeedMultiplier => _inputController.PlayerMovementSpeedMultiplier;
    internal WorldLightingMode WorldLightingMode => _worldLighting.Mode;
    internal SacredParticleQuality ParticleQuality => _particles.Quality;
    internal Vector2 PlayerWorldPosition => _camera.WorldCenter;
    internal bool WorldStreamingSettled => _worldStreamer.VisibleWorld.LoadingSectors == 0;

    internal void SetWorldLightingMode(WorldLightingMode mode) => _worldLighting.SetMode(mode);

    internal void SetParticleQuality(SacredParticleQuality quality) => _particles.SetQuality(quality);

    internal void SetCollisionMode(CollisionCheatMode mode) => _inputController.SetCollisionMode(mode);

    internal void SetPlayerMovementSpeedMultiplier(float value) =>
        _inputController.SetPlayerMovementSpeedMultiplier(value);

    internal void SetNoClipEnabled(bool enabled) =>
        SetCollisionMode(enabled ? CollisionCheatMode.NoClip : CollisionCheatMode.Walk);

    internal PlayerDebugPanelState CreatePlayerDebugPanelState() => _player.CreateDebugPanelState();

    internal bool RemovePlayerEquipment(int slotIndex) => _player.RemoveEquipment(slotIndex);

    internal bool EquipPlayerItemSet(int setIndex) => _player.EquipItemSet(setIndex);

    internal bool SelectPlayerCharacter(uint entryId) => _player.SelectModel(entryId);

    internal Task<TextureAsset> LoadTextureAsync(string textureName, CancellationToken cancellationToken) =>
        _assets.LoadTextureAsync(textureName, cancellationToken);

    internal void Teleport(Vector2 destination) => _inputController.Teleport(destination);

    internal bool TryStartElevationTrace(string route, out string message) =>
        _inputController.TryStartElevationTrace(route, out message);

    internal bool TrySetCheatOption(string option, string value, out string message)
    {
        switch (option.ToLowerInvariant())
        {
            case "overlays" or "debug-overlay" when TryParseBoolean(value, out var overlaysVisible):
                _scene.Debug.OverlaysVisible = overlaysVisible;
                message = $"debug overlays {(overlaysVisible ? "visible" : "hidden")}";
                return true;
            case "debug-panel" or "panel" when TryParseBoolean(value, out var panelVisible):
                _scene.Debug.PanelVisible = panelVisible;
                message = $"ImGui debug panel {(panelVisible ? "visible" : "hidden")}";
                return true;
            case "lighting" when TryParseLightingMode(value, out var lightingMode):
                _worldLighting.SetMode(lightingMode);
                message = $"world lighting set to {_worldLighting.DisplayName}";
                return true;
            case "stairs" or "stairs-tiles" when TryParseBoolean(value, out var gateTilesVisible):
                _scene.Debug.StairsMapVisible = gateTilesVisible;
                message = $"stairs and door tiles {(gateTilesVisible ? "visible" : "hidden")}";
                return true;
            case "blocked" or "blocked-tiles" when TryParseBoolean(value, out var blockedTilesVisible):
                _scene.Debug.BlockedAreasVisible = blockedTilesVisible;
                message = $"blocked tiles {(blockedTilesVisible ? "visible" : "hidden")}";
                return true;
            case "noclip" or "no-clip" when TryParseBoolean(value, out var noClipEnabled):
                SetNoClipEnabled(noClipEnabled);
                message = $"noclip {(noClipEnabled ? "enabled" : "disabled")}";
                return true;
            case "collision" when TryParseCollisionMode(value, out var collisionMode):
                SetCollisionMode(collisionMode);
                message = $"collision set to {collisionMode}";
                return true;
            case "tessellation" or "tess" or "vertices" when TryParseBoolean(value, out var topologyVisible):
                _scene.Debug.TerrainTopologyVisible = topologyVisible;
                message = topologyVisible
                    ? "terrain topology visible: cyan=perimeter, magenta=native top-bottom diagonal, yellow=vertices"
                    : "terrain topology hidden";
                return true;
            case "item-flags" or "item-flag" when TryParseItemGraphicFlags(value, out var itemFlags):
                _scene.Debug.VisibleItemGraphicFlags = itemFlags;
                message = $"Items.pak graphic flag overlay set to 0x{(ushort)itemFlags:X4}";
                return true;
            case "character" when value.Equals("next", StringComparison.OrdinalIgnoreCase):
                _player.CycleModel();
                message = "loading next character";
                return true;
            case "character" when uint.TryParse(value, out var characterIndex) && _player.SelectModel(characterIndex):
                message = $"loading character {characterIndex}";
                return true;
            case "zoom" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom) && float.IsFinite(zoom):
                _camera.CenterOnTile(_camera.WorldCenter.X, _camera.WorldCenter.Y, zoom);
                message = $"camera zoom set to {zoom}";
                return true;
            case "animation" when value.Equals("attack", StringComparison.OrdinalIgnoreCase):
                _player.PlayAttack();
                message = "playing attack animation";
                return true;
            case "particles" when TryParseBoolean(value, out var particlesEnabled):
                _particles.Enabled = particlesEnabled;
                message = $"world particles {(particlesEnabled ? "enabled" : "disabled")}";
                return true;
            default:
                message = "Unknown in-game option. Use overlays <on|off>, debug-panel <on|off>, lighting <day|night|cycle|black>, stairs <on|off>, blocked <on|off>, collision <walk|fly|noclip>, noclip <on|off>, tessellation <on|off>, particles <on|off>, item-flags <hex>, character <next|index>, zoom <0.25..3>, or animation attack.";
                return false;
        }
    }

    private static bool TryParseCollisionMode(string value, out CollisionCheatMode mode) =>
        Enum.TryParse(value, ignoreCase: true, out mode);

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
    public void Update(float deltaSeconds)
    {
        _inputController.Update(deltaSeconds);
        _doors.Update(_worldStreamer.VisibleWorld, _camera.WorldCenter, deltaSeconds);
        _particles.Update(deltaSeconds, _worldStreamer.VisibleWorld);
        UpdateRegionDisplayName();
    }

    public ValueTask RenderAsync(SceneRenderContext context) =>
        context.Renderer.RenderFrameAsync(
            _camera,
            _worldStreamer.VisibleWorld,
            _scene,
            _particles.Particles,
            _particles.Revision,
            context.VerticalSyncEnabled,
            context.FramePacingStatus,
            context.FrameId,
            context.CancellationToken);

    public WorldPreloadRequest CreatePreloadRequest()
    {
        // The loading scene renders preload frames before this scene receives Update.
        // Select and request its initial world models here, so doors and containers
        // do not wait for the player to cross a sector boundary.
        _doors.Update(_worldStreamer.VisibleWorld, _camera.WorldCenter, 0.0f);
        return new WorldPreloadRequest(_camera, _worldStreamer.VisibleWorld, _scene);
    }

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
        _inputController.InitializeLocation(startLocation);
        UpdateRegionDisplayName();
        var zone = _scene.Indoor.ActiveGroup is null
            ? _worldStreamer.GetZone(_camera.WorldCenter)
            : WorldZone.Indoors;
        _worldLighting.Update(0.0f, _scene.Lighting, new Vector3(_camera.WorldCenter, 0.0f), zone);
        _player.Initialize(_camera.WorldCenter);
    }

    private void UpdateRegionDisplayName()
    {
        const string regionDisplayName = "";
        _scene.Minimap.RegionDisplayName = regionDisplayName;
        if (string.Equals(_lastRegionDisplayName, regionDisplayName, StringComparison.Ordinal))
            return;

        _lastRegionDisplayName = regionDisplayName;
        EngineLog.WriteLine($"Minimap region changed: {regionDisplayName}.");
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

    private static bool TryParseItemGraphicFlags(string value, out SacredItemGraphicFlags flags)
    {
        var digits = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        var parsed = ushort.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var bits);
        flags = (SacredItemGraphicFlags)bits;
        return parsed;
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
