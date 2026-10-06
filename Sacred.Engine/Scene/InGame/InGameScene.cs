using System;
using System.Globalization;
using System.Linq;
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
using Sacred.Granny.Diagnostics;
using Sacred.Particles;
using Sacred.World;
using Sacred.World.Objects;
using Sacred.World.Particles;
using Sacred.World.Portals;

namespace Sacred.Engine.Scene.InGame;

internal sealed class InGameScene : IGameScene
{
    private readonly AnimatedModelDebugCrowd _debugCrowd = new();
    private readonly AssetManager _assets;
    private readonly WorldStreamer _worldStreamer;
    private readonly WorldParticleSystem _particles;
    private readonly PlayerParticleEffectsController _playerParticles;
    private readonly PlayerFootprintController _footprints;
    private readonly WorldPortalScriptIndex _portalScript;
    private readonly SacredCamera _camera;
    private readonly SceneState _scene = new();
    private readonly PlayerCharacterController _player;
    private readonly DoorSceneController _doors;
    private readonly WorldLightingController _worldLighting;
    private readonly InGameInputController _inputController;
    private readonly Win32Window _window;
    private readonly SacredGameSaveState _saveState;
    internal WorldCampaignScripts? CampaignScripts { get; }
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
        CampaignScripts = resources.WorldArchive.CampaignScripts;
        _worldStreamer = new WorldStreamer(resources.WorldArchive);
        _particles = new WorldParticleSystem(resources.WorldArchive.ParticleScript, saveState.ParticleQuality);
        _particles.GpuBackend = renderer.ParticleGpuBackend;
        _particles.EmissionBackend = renderer.WorldEmissionBackend;
        _particles.SetSimulationMode(saveState.ParticleSimulation);
        _playerParticles = new PlayerParticleEffectsController(_particles, _scene, new WorldElevationSampler(_worldStreamer));
        _camera = SacredCamera.CreateDefault(window.ClientWidth, window.ClientHeight);
        _footprints = new PlayerFootprintController(_assets, _worldStreamer, _scene);
        _player = new PlayerCharacterController(_assets, _scene, saveState.CharacterName);
        _doors = new DoorSceneController(_assets, _scene, new WorldDoorStateLayer(
            resources.WorldArchive.ObjectScript.Placements.Where(p => p.Door is not null).Select(p => p.Door!)),
            () => _camera.WorldCenter);
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
        _inputController.SetPlayerMovementSpeedMultiplier(saveState.PlayerMovementSpeedMultiplier);
        _inputController.Portals = new PortalTraversalController(resources.WorldArchive.Portals);
        _portalScript = resources.WorldArchive.Portals;
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
    internal ParticleSimulationMode ParticleSimulation => _particles.SimulationMode;
    internal Vector2 PlayerWorldPosition => _camera.WorldCenter;
    internal bool WorldStreamingSettled => _worldStreamer.VisibleWorld.LoadingSectors == 0 &&
        Renderer.LastWorldPreparationStatus.IsReady && !_doors.HasPendingLoads;

    internal void SetWorldLightingMode(WorldLightingMode mode) => _worldLighting.SetMode(mode);

    internal void SetParticleQuality(SacredParticleQuality quality) => _particles.SetQuality(quality);
    internal void SetParticleSimulation(ParticleSimulationMode mode) => _particles.SetSimulationMode(mode);
    internal void SetWorldEmission(bool enabled)
    {
        if (enabled) { Renderer.SetWorldEmission(true); _particles.SetGpuEmission(true); }
        else { _particles.SetGpuEmission(false); Renderer.SetWorldEmission(false); }
    }

    internal void SetCollisionMode(CollisionCheatMode mode) => _inputController.SetCollisionMode(mode);

    internal void SetPlayerMovementSpeedMultiplier(float value) =>
        _inputController.SetPlayerMovementSpeedMultiplier(value);

    internal void SetNoClipEnabled(bool enabled) =>
        SetCollisionMode(enabled ? CollisionCheatMode.NoClip : CollisionCheatMode.Walk);

    internal PlayerDebugPanelState CreatePlayerDebugPanelState() => _player.CreateDebugPanelState();
    internal PlayerParticlePanelState CreatePlayerParticlePanelState() => _playerParticles.CreateState(_camera.WorldCenter);
    internal bool ExecutePlayerParticleRequest(PlayerParticleRequest request) =>
        _playerParticles.Execute(request, _camera.WorldCenter);

    internal bool RemovePlayerEquipment(int slotIndex) => _player.RemoveEquipment(slotIndex);
    internal bool RemoveAllPlayerEquipment() => _player.RemoveAllEquipment();

    internal bool EquipPlayerItemSet(int setIndex) => _player.EquipItemSet(setIndex);

    internal bool SelectPlayerCharacter(uint entryId) => _player.SelectModel(entryId);

    internal Task<TextureAsset> LoadTextureAsync(string textureName, CancellationToken cancellationToken) =>
        _assets.LoadTextureAsync(textureName, cancellationToken);

    internal void Teleport(Vector2 destination)
    {
        _inputController.Teleport(destination);
        _footprints.Reset(destination);
    }

    internal bool TryStartElevationTrace(string route, out string message) =>
        _inputController.TryStartElevationTrace(route, out message);

    internal bool TrySetCheatOption(string option, string value, out string message)
    {
        if (_playerParticles.TrySetCheatOption(option, value, _camera.WorldCenter, out message)) return true;
        if (PlayerMovementCheats.TrySetOption(option, value, _camera, _inputController, out message)) return true;
        if (WorldFocusCheats.TrySetOption(option, value, _inputController, _doors, _camera,
                _scene.Indoor.ActiveGroup?.SurfaceLevel ?? 0, out message)) return true;
        switch (option.ToLowerInvariant())
        {
            case "render-time" when value.Equals("live", StringComparison.OrdinalIgnoreCase):
                Renderer.SetOffscreenAnimationTime(float.NaN);
                message = "render animation clock resumed"; return true;
            case "render-time" when float.TryParse(value, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var renderTime) &&
                    float.IsFinite(renderTime) && renderTime >= 0:
                Renderer.SetOffscreenAnimationTime(renderTime);
                message = $"render animation clock frozen at {renderTime:0.###} seconds"; return true;
            case "portals" when value.Equals("status", StringComparison.OrdinalIgnoreCase):
                foreach (var portal in _portalScript.Billboards)
                    EngineLog.WriteLine($"Portal billboard: world {portal.Position.X:0.###},{portal.Position.Y:0.###}; tile {portal.TileX},{portal.TileY}; {(portal.IsStartup ? "StartCode" : "FunkCode")}+0x{portal.ScriptOffset:X}; {portal.Variant}; unlit animated.");
                foreach (var trigger in _portalScript.Triggers.Where(t => t.HasPortalMapIcon))
                    EngineLog.WriteLine($"Portal trigger: {trigger.Name}; {trigger.MinimumX},{trigger.MinimumY}..{trigger.MaximumX},{trigger.MaximumY}; level {trigger.SurfaceLevel}; destination {trigger.Destination.X},{trigger.Destination.Y},{trigger.Destination.Z}.");
                message = $"default portals: {_portalScript.Billboards.Count} billboards, {_portalScript.Triggers.Count(t => t.HasPortalMapIcon)} marked triggers; {_portalScript.Triggers.Count} total script transitions";
                return true;
            case "transitions" when value.Equals("status", StringComparison.OrdinalIgnoreCase):
                foreach (var trigger in _portalScript.Triggers.Where(t =>
                    Math.Abs(t.MinimumX - _camera.WorldCenter.X) < 32 && Math.Abs(t.MinimumY - _camera.WorldCenter.Y) < 32))
                    EngineLog.WriteLine($"Nearby script transition: {trigger.Name}; {trigger.MinimumX},{trigger.MinimumY}..{trigger.MaximumX},{trigger.MaximumY}; level {trigger.SurfaceLevel}; target {trigger.Destination.X},{trigger.Destination.Y} level {trigger.Destination.Z}.");
                message = $"player {_camera.WorldCenter.X:0.##},{_camera.WorldCenter.Y:0.##}; surface {_scene.Indoor.ActiveGroup?.SurfaceLevel ?? 0}; group {_scene.Indoor.ActiveGroup?.Id.ToString() ?? "exterior"}; height {_scene.Debug.ActorTerrainHeight:0.###}";
                return true;
            case "footprints" when TryParseBoolean(value, out var footprintsEnabled):
                _footprints.Enabled = footprintsEnabled;
                _footprints.Clear();
                message = _footprints.Status;
                return true;
            case "footprints" when value.Equals("clear", StringComparison.OrdinalIgnoreCase):
                _footprints.Clear(); message = _footprints.Status; return true;
            case "footprints" when value.Equals("status", StringComparison.OrdinalIgnoreCase):
                message = _footprints.Status; return true;
            case "overlays" or "debug-overlay" when TryParseBoolean(value, out var overlaysVisible):
                _scene.Debug.OverlaysVisible = overlaysVisible;
                message = $"debug overlays {(overlaysVisible ? "visible" : "hidden")}";
                return true;
            case "debug-panel" or "panel" when TryParseBoolean(value, out var panelVisible):
                _scene.Debug.PanelVisible = panelVisible;
                message = $"ImGui debug panel {(panelVisible ? "visible" : "hidden")}";
                return true;
            case "indoor-paths" when TryParseBoolean(value, out var indoorPathsVisible):
                _scene.Debug.IndoorNavigationVisible = indoorPathsVisible;
                message = $"active floor navigation {(indoorPathsVisible ? "visible" : "hidden")}";
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
            case "skin-crowd" when int.TryParse(value, out var crowdCount) && crowdCount >= 0 && crowdCount <= 128:
                _debugCrowd.Count = crowdCount;
                message = $"independent animated debug instances: {crowdCount}";
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
            case "facing" when float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture,
                out var facingDegrees) && float.IsFinite(facingDegrees):
                var facingRadians = facingDegrees * (MathF.PI / 180.0f);
                _camera.RotateToward(new Vector2(MathF.Cos(facingRadians), MathF.Sin(facingRadians)));
                EngineLog.WriteLine($"Debug input: character facing set to {facingDegrees:F1} degrees.");
                message = $"character facing set to {facingDegrees:F1} degrees";
                return true;
            case "world-particle-step" when value.Equals("off", StringComparison.OrdinalIgnoreCase):
                _particles.SetFixedStep(null); message = "World particle step follows frame time"; return true;
            case "world-particle-step" when float.TryParse(value, NumberStyles.Float,
                CultureInfo.InvariantCulture, out var fixedParticleStep) && float.IsFinite(fixedParticleStep) && fixedParticleStep is >= 0 and <= .1f:
                _particles.SetFixedStep(fixedParticleStep); message = $"World particle fixed step {fixedParticleStep} seconds"; return true;
            case "particle-quality" when Enum.TryParse<SacredParticleQuality>(value, true, out var particleQuality) && Enum.IsDefined(particleQuality):
                _particles.SetQuality(particleQuality); message = $"World particle quality {particleQuality}"; return true;
            case "indoor-floor" when value.Equals("off", StringComparison.OrdinalIgnoreCase):
                _scene.Indoor.ActiveGroup = null; message = "Indoor floor override cleared"; return true;
            case "indoor-floor" when byte.TryParse(value, out var floorLevel):
                var floor = _worldStreamer.VisibleWorld.Sectors.SelectMany(s => s.IndoorTileGroups.Groups)
                    .FirstOrDefault(g => g.SurfaceLevel == floorLevel && g.TryGetLocalTile(
                        (int)MathF.Floor(_camera.WorldCenter.X), (int)MathF.Floor(_camera.WorldCenter.Y), out _, out _));
                if (floor is null) { message = "No decoded floor at the current tile and level"; return false; }
                _scene.Indoor.ActiveGroup = floor; message = $"Indoor floor override: {floor.Id}; level {floorLevel}"; return true;
            case "particles" when TryParseBoolean(value, out var particlesEnabled):
                _particles.Enabled = particlesEnabled;
                message = $"world particles {(particlesEnabled ? "enabled" : "disabled")}";
                return true;
            case "particle-simulation" when Enum.TryParse<ParticleSimulationMode>(value, true, out var simulation) && Enum.IsDefined(simulation):
                _particles.SetSimulationMode(simulation);
                message = $"particle simulation requested {_particles.SimulationMode}; selected {_particles.SelectedSimulationMode}";
                return true;
            case "door" when value.Equals("toggle", StringComparison.OrdinalIgnoreCase):
                var toggled = _doors.TryToggleAt(_camera.WorldCenter);
                message = toggled ? "nearest door interaction handled" : "no door within interaction distance";
                EngineLog.WriteLine($"Debug input: {message}.");
                return true;
            case "door-list":
                foreach (var door in _doors.DebugStates.OrderBy(d => Vector2.DistanceSquared(d.Position, _camera.WorldCenter)))
                    EngineLog.WriteLine($"Door state: static {door.Id}; trigger {door.Trigger}; world {door.Position.X:0.###},{door.Position.Y:0.###}; open {door.Open}; locked {door.Locked}.");
                foreach (var door in _doors.DebugDoors.OrderBy(d => Vector2.DistanceSquared(d.Position, _camera.WorldCenter)))
                    EngineLog.WriteLine($"Door playback: {door.Id}; world {door.Position.X.ToString("R", CultureInfo.InvariantCulture)},{door.Position.Y.ToString("R", CultureInfo.InvariantCulture)}; open {door.Playback.CanActivate}; close {door.Playback.CanReset}; pose {door.Playback.Pose?.Revision ?? 0}");
                message = "loaded door playback candidates listed";
                return true;
            default:
                message = "Unknown in-game option. Use overlays <on|off>, debug-panel <on|off>, lighting <day|night|cycle|black>, stairs <on|off>, blocked <on|off>, collision <walk|fly|noclip>, noclip <on|off>, tessellation <on|off>, particles <on|off>, particle-simulation <Auto|CpuSimd|CpuScalar|Gpu|GpuOnly>, item-flags <hex>, character <next|index>, zoom <0.25..3>, facing <degrees>, path <x>,<y>, door toggle, door-list show, or animation attack.";
                return false;
        }
    }

    private static bool TryParseCollisionMode(string value, out CollisionCheatMode mode) =>
        Enum.TryParse(value, ignoreCase: true, out mode);

    public SacredCamera Camera => _camera;
    public float CameraZoom => _camera.Zoom;

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
        _footprints.Update(deltaSeconds, _camera);
        Renderer.UpdateAutoRenderResolution(_camera.Zoom);
        _doors.Update(_worldStreamer.VisibleWorld, _camera.WorldCenter, deltaSeconds, _scene.Indoor.ActiveGroup);
        _debugCrowd.Update(_scene, deltaSeconds);
        _playerParticles.Update();
        using (AnimationPerformance.Measure(AnimationCpuStage.Particles))
        _particles.Update(deltaSeconds, _worldStreamer.VisibleWorld, _scene.Indoor.ActiveGroup,
            _camera.WorldCenter, _playerParticles.SelfHeight);
        _scene.GpuParticlesEnabled = _particles.GpuEmissionEnabled || _particles.SelectedSimulationMode is ParticleSimulationMode.Gpu or ParticleSimulationMode.GpuOnly;
        _scene.GpuWorldEmitters = _particles.GpuEmitters;
        _scene.GpuParticleBatches = _particles.GpuBatches;
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
        _doors.Update(_worldStreamer.VisibleWorld, _camera.WorldCenter, 0.0f, _scene.Indoor.ActiveGroup);
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
        _worldLighting.Update(0.0f, _scene.Lighting, zone);
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
