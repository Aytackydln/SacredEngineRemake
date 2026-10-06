using System;
using System.IO;
using System.Numerics;
using Sacred.Core;
using Sacred.Engine.Assets;
using Sacred.Engine.Cheats;
using Sacred.Engine.Graphics;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Diagnostics;
using Sacred.Particles;
using Sacred.Particles.Diagnostics;

namespace Sacred.Engine;

/// <summary>Owns scene construction, engine-wide input, cheats, and persistent runtime state.</summary>
internal sealed partial class SacredGameRuntime : IDisposable
{
    private readonly Win32Window _window;
    private readonly LowLatencySystem _latency;
    private readonly Dx12Renderer _renderer;
    private readonly FramePacingController _framePacing;
    private readonly SceneManager _scenes;
    private GameResourceLoader _resourceLoader;
    private readonly GamepadInputSource _gamepad = new();
    private readonly EngineInputController _engineInput;
    private readonly CheatsController _cheats;
    private readonly string _gameDirectory;
    private readonly SacredGameSaveState _initialSaveState;
    private readonly DebugUiControlState _debugUiControls;

    private readonly WorldMapControls _worldMapControls = new();
    private InGameScene? _inGameScene;
    private PendingInspection? _pendingInspection;
    private GrnBackendKind _grannyBackend;
    private bool _disposed;

    public SacredGameRuntime(
        SacredGameDirectories gameDirectories,
        SacredGameSaveState initialSaveState,
        string gameDirectory,
        Win32Window window,
        LowLatencySystem latency,
        Dx12Renderer renderer,
        FramePacingController framePacing,
        SceneManager scenes)
    {
        _initialSaveState = initialSaveState;
        window.FocusRequestsEnabled = initialSaveState.RequestWindowFocus;
        _grannyBackend = initialSaveState.GrannyBackend;
        _gameDirectory = gameDirectory;
        _window = window;
        _latency = latency;
        _renderer = renderer;
        _debugUiControls = renderer.DebugUiControls;
        _framePacing = framePacing;
        _scenes = scenes;
        InitializeCampaignSelection(gameDirectories, initialSaveState);
        _resourceLoader = new GameResourceLoader(gameDirectories, _grannyBackend)
        {
            CampaignScriptsDirectory = _campaignSelection
        };
        _engineInput = new EngineInputController(
            window.Input,
            renderer,
            latency,
            framePacing.CycleMode,
            window.ToggleBorderlessFullscreen);
        _cheats = new CheatsController(Console.In);

        SynchronizeDebugUiControls();

        RegisterScenes();
        scenes.SceneChanged += () =>
        {
        };
        scenes.Start(GameSceneId.InitialLoading);
        window.RequestFocus();
    }

    public void Update(float deltaSeconds)
    {
        ApplyCampaignRequests();
        ApplyDebugUiRequests();
        _gamepad.Poll(_window.Input);
        _cheats.Update(ExecuteCheat);
        if (_window.Input.ConsumePressed(VirtualKey.F12))
            CaptureScreenshot(null);
        _engineInput.Update();
        _scenes.Update(deltaSeconds);
        ApplyWorldMapRequest();
        SynchronizeDebugUiControls();
        UpdatePendingInspection();
    }

    private void ApplyDebugUiRequests()
    {
        if (_debugUiControls.RequestedSkinningMode is { } skinningMode)
        {
            _debugUiControls.RequestedSkinningMode = null;
            if (_renderer.WorldInitialized) _renderer.SetSkinningMode(skinningMode);
            EngineLog.WriteLine($"Debug input: model skinning {skinningMode}");
        }
        if (_debugUiControls.RequestedParticleSimulation is { } particleSimulation)
        {
            _debugUiControls.RequestedParticleSimulation = null;
            _inGameScene?.SetParticleSimulation(particleSimulation);
        }
        if (_debugUiControls.RequestedHdrEnabled is { } hdrEnabled)
        {
            _debugUiControls.RequestedHdrEnabled = null;
            if (_renderer.IsHdrEnabled != hdrEnabled)
                _renderer.ToggleHdr();
        }

        if (_debugUiControls.RequestedFramePacingMode is { } framePacingMode)
        {
            _debugUiControls.RequestedFramePacingMode = null;
            _framePacing.SetMode(framePacingMode);
        }

        if (_debugUiControls.RequestedManualFrameRate is { } manualFrameRate)
        {
            _debugUiControls.RequestedManualFrameRate = null;
            _framePacing.SetManualFrameRate(manualFrameRate);
        }

        if (_debugUiControls.RequestedLowLatencyMode is { } lowLatencyMode)
        {
            _debugUiControls.RequestedLowLatencyMode = null;
            _framePacing.SetLowLatencyMode(lowLatencyMode);
        }

        if (_debugUiControls.RequestedWorldLightingMode is { } worldLightingMode)
        {
            _debugUiControls.RequestedWorldLightingMode = null;
            _inGameScene?.SetWorldLightingMode(worldLightingMode);
        }

        if (_debugUiControls.RequestedBorderlessFullscreen is { } fullscreen)
        {
            _debugUiControls.RequestedBorderlessFullscreen = null;
            _window.SetBorderlessFullscreen(fullscreen);
        }

        if (_debugUiControls.RequestedParticleQuality is { } particleQuality)
        {
            _debugUiControls.RequestedParticleQuality = null;
            _inGameScene?.SetParticleQuality(particleQuality);
        }

        if (_debugUiControls.RequestedRenderResolutionPercentage is { } renderResolutionPercentage)
        {
            _debugUiControls.RequestedRenderResolutionPercentage = null;
            _renderer.SetRenderResolutionPercentage(renderResolutionPercentage);
        }

        if (_debugUiControls.RequestedAutoRenderResolution is { } autoResolution)
        {
            _debugUiControls.RequestedAutoRenderResolution = null;
            _renderer.SetAutoRenderResolution(autoResolution);
        }

        if (_debugUiControls.RequestedAutoRenderResolutionRange is { } autoResolutionRange)
        {
            _debugUiControls.RequestedAutoRenderResolutionRange = null;
            _renderer.SetAutoRenderResolutionRange(autoResolutionRange.Minimum, autoResolutionRange.Maximum);
        }

        if (_debugUiControls.RequestedAutoRenderResolutionStepSnapping is { } autoResolutionStepSnapping)
        {
            _debugUiControls.RequestedAutoRenderResolutionStepSnapping = null;
            _renderer.SetAutoRenderResolutionStepSnapping(
                autoResolutionStepSnapping.Enabled,
                autoResolutionStepSnapping.StepPercentage);
        }

        if (_debugUiControls.RequestedRenderScalingMode is { } renderScalingMode)
        {
            _debugUiControls.RequestedRenderScalingMode = null;
            _renderer.SetRenderScalingMode(renderScalingMode);
        }

        if (_debugUiControls.RequestedCollisionMode is { } collisionMode)
        {
            _debugUiControls.RequestedCollisionMode = null;
            _inGameScene?.SetCollisionMode(collisionMode);
        }

        if (_debugUiControls.RequestedPlayerMovementSpeedMultiplier is { } movementSpeedMultiplier)
        {
            _debugUiControls.RequestedPlayerMovementSpeedMultiplier = null;
            _inGameScene?.SetPlayerMovementSpeedMultiplier(movementSpeedMultiplier);
        }

        if (_debugUiControls.RequestedPlayerEquipmentRemoval is { } equipmentSlot)
        {
            _debugUiControls.RequestedPlayerEquipmentRemoval = null;
            _inGameScene?.RemovePlayerEquipment(equipmentSlot);
        }

        if (_debugUiControls.RequestedPlayerRemoveAllEquipment)
        {
            _debugUiControls.RequestedPlayerRemoveAllEquipment = false;
            _inGameScene?.RemoveAllPlayerEquipment();
        }

        if (_debugUiControls.RequestedPlayerItemSet is { } itemSet)
        {
            _debugUiControls.RequestedPlayerItemSet = null;
            _inGameScene?.EquipPlayerItemSet(itemSet);
        }

        if (_debugUiControls.RequestedPlayerCharacter is { } characterEntryId)
        {
            _debugUiControls.RequestedPlayerCharacter = null;
            _inGameScene?.SelectPlayerCharacter(characterEntryId);
        }

        while (_debugUiControls.ParticleRequests.TryDequeue(out var particleRequest))
            _inGameScene?.ExecutePlayerParticleRequest(particleRequest);

        if (_debugUiControls.ScreenshotRequested)
        {
            _debugUiControls.ScreenshotRequested = false;
            CaptureScreenshot(null);
        }
    }

    private void SynchronizeDebugUiControls()
    {
        SynchronizeCampaignControls();
        _debugUiControls.HdrEnabled = _renderer.IsHdrEnabled;
        _debugUiControls.FramePacingMode = _framePacing.Mode;
        _debugUiControls.ManualFrameRate = _framePacing.ManualFrameRate;
        _debugUiControls.LowLatencyMode = _latency.Mode;
        _debugUiControls.WorldLightingMode =
            _inGameScene?.WorldLightingMode ?? _initialSaveState.WorldLightingMode;
        _debugUiControls.BorderlessFullscreen = _window.IsBorderlessFullscreen;
        _debugUiControls.ParticleQuality = _inGameScene?.ParticleQuality ?? _initialSaveState.ParticleQuality;
        _debugUiControls.ParticleSimulation = _inGameScene?.ParticleSimulation ?? _initialSaveState.ParticleSimulation;
        _debugUiControls.SkinningMode = _renderer.SkinningMode;
        _debugUiControls.CollisionMode = _inGameScene?.CollisionMode ?? CollisionCheatMode.Walk;
        _debugUiControls.PlayerMovementSpeedMultiplier =
            _inGameScene?.PlayerMovementSpeedMultiplier ?? _initialSaveState.PlayerMovementSpeedMultiplier;
        _debugUiControls.RenderResolutionPercentage = (int)MathF.Round(_renderer.RenderResolutionPercentage * 100);
        _debugUiControls.AutoRenderResolution = _renderer.AutoRenderResolution;
        _debugUiControls.AutoRenderResolutionMinimumPercentage =
            _renderer.AutoRenderResolutionMinimumPercentage;
        _debugUiControls.AutoRenderResolutionMaximumPercentage =
            _renderer.AutoRenderResolutionMaximumPercentage;
        _debugUiControls.AutoRenderResolutionStepSnapping =
            _renderer.AutoRenderResolutionStepSnapping;
        _debugUiControls.AutoRenderResolutionStepPercentage =
            _renderer.AutoRenderResolutionStepPercentage;
        _debugUiControls.RenderScalingMode = _renderer.RenderScalingMode;
        _debugUiControls.Player = _inGameScene?.CreatePlayerDebugPanelState();
        _debugUiControls.PlayerParticles = _inGameScene?.CreatePlayerParticlePanelState();
    }

    public SacredGameSaveState CaptureSaveState()
    {
        var windowedBounds = _window.CaptureWindowedBounds();
        return new SacredGameSaveState
        {
            BorderlessFullscreen = _window.IsBorderlessFullscreen,
            WindowedWidth = windowedBounds.Width,
            WindowedHeight = windowedBounds.Height,
            WindowedX = windowedBounds.X,
            WindowedY = windowedBounds.Y,
            WindowedMaximized = windowedBounds.Maximized,
            HdrEnabled = _renderer.IsHdrEnabled,
            HdrBrightness = _renderer.HdrBrightnessSettings,
            FramePacingMode = _framePacing.Mode,
            ManualFrameRate = _framePacing.ManualFrameRate,
            LowLatencyMode = _latency.Mode,
            RenderResolutionPercentage = (int)MathF.Round(_renderer.RenderResolutionPercentage * 100),
            AutoRenderResolution = _renderer.AutoRenderResolution,
            AutoRenderResolutionMinimumPercentage = _renderer.AutoRenderResolutionMinimumPercentage,
            AutoRenderResolutionMaximumPercentage = _renderer.AutoRenderResolutionMaximumPercentage,
            AutoRenderResolutionStepSnapping = _renderer.AutoRenderResolutionStepSnapping,
            AutoRenderResolutionStepPercentage = _renderer.AutoRenderResolutionStepPercentage,
            RenderScalingMode = _renderer.RenderScalingMode,
            GrannyBackend = _grannyBackend,
            WorldLightingMode = _inGameScene?.WorldLightingMode ?? _initialSaveState.WorldLightingMode,
            StairsTilesVisible = _inGameScene?.StairsTilesVisible ?? _initialSaveState.StairsTilesVisible,
            BlockedTilesVisible = _inGameScene?.BlockedTilesVisible ?? _initialSaveState.BlockedTilesVisible,
            PlayerMovementSpeedMultiplier =
                _inGameScene?.PlayerMovementSpeedMultiplier ?? _initialSaveState.PlayerMovementSpeedMultiplier,
            ParticleQuality = _inGameScene?.ParticleQuality ?? _initialSaveState.ParticleQuality,
            ParticleSimulation = _inGameScene?.ParticleSimulation ?? _initialSaveState.ParticleSimulation,
            SkinningMode = _renderer.WorldInitialized ? _renderer.SkinningMode : _initialSaveState.SkinningMode,
            CharacterName = _inGameScene?.SelectedCharacterName ?? _initialSaveState.CharacterName,
            CampaignScriptsDirectory = _campaignSelection,
            LastLocation = _inGameScene?.PlayerWorldPosition ?? _initialSaveState.LastLocation
        };
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cheats.Dispose();
        _scenes.Dispose();
        _resourceLoader.Dispose();
    }

    public static string ResolveGameDirectory(SacredGameDirectories gameDirectories)
    {
        var pakDirectory = Path.GetDirectoryName(gameDirectories.TexturesPakPath);
        return Path.GetDirectoryName(pakDirectory) ?? ".";
    }

    public static SacredGameSaveState NormalizeSaveState(SacredGameSaveState state)
    {
        Vector2? location = state.LastLocation is { } savedLocation &&
                            float.IsFinite(savedLocation.X) &&
                            float.IsFinite(savedLocation.Y)
            ? savedLocation
            : null;

        return state with
        {
            WindowedWidth = NormalizeWindowDimension(state.WindowedWidth, 1600),
            WindowedHeight = NormalizeWindowDimension(state.WindowedHeight, 900),
            PlayerMovementSpeedMultiplier = NormalizePlayerMovementSpeedMultiplier(state.PlayerMovementSpeedMultiplier),
            ParticleQuality = Enum.IsDefined(state.ParticleQuality)
                ? state.ParticleQuality
                : SacredParticleQuality.High,
            ParticleSimulation = Enum.IsDefined(state.ParticleSimulation)
                ? state.ParticleSimulation : ParticleSimulationMode.Auto,
            SkinningMode = Enum.IsDefined(state.SkinningMode) ? state.SkinningMode : SkinningMode.Auto,
            HdrBrightness = (state.HdrBrightness ?? HdrBrightnessSettings.Default).Normalized(),
            FramePacingMode = Enum.IsDefined(state.FramePacingMode)
                ? state.FramePacingMode
                : FramePacingMode.VariableRefreshRate,
            ManualFrameRate = Math.Clamp(
                state.ManualFrameRate,
                FramePacingController.MinimumManualFrameRate,
                FramePacingController.MaximumManualFrameRate),
            LowLatencyMode = Enum.IsDefined(state.LowLatencyMode)
                ? state.LowLatencyMode
                : LowLatencyMode.On,
            RenderResolutionPercentage = Math.Clamp(
                state.RenderResolutionPercentage,
                TileResolutionScaling.MinimumPercentage,
                TileResolutionScaling.MaximumPercentage),
            AutoRenderResolution = state.AutoRenderResolution,
            AutoRenderResolutionMinimumPercentage = Math.Clamp(
                Math.Min(
                    state.AutoRenderResolutionMinimumPercentage,
                    state.AutoRenderResolutionMaximumPercentage),
                TileResolutionScaling.MinimumPercentage,
                TileResolutionScaling.MaximumPercentage),
            AutoRenderResolutionMaximumPercentage = Math.Clamp(
                Math.Max(
                    state.AutoRenderResolutionMinimumPercentage,
                    state.AutoRenderResolutionMaximumPercentage),
                TileResolutionScaling.MinimumPercentage,
                TileResolutionScaling.MaximumPercentage),
            AutoRenderResolutionStepSnapping = state.AutoRenderResolutionStepSnapping,
            AutoRenderResolutionStepPercentage = Math.Clamp(
                state.AutoRenderResolutionStepPercentage,
                TileResolutionScaling.MinimumStepPercentage,
                TileResolutionScaling.MaximumStepPercentage),
            RenderScalingMode = Enum.IsDefined(state.RenderScalingMode)
                ? state.RenderScalingMode
                : RenderScalingMode.Bilinear,
            GrannyBackend = Enum.IsDefined(state.GrannyBackend)
                ? state.GrannyBackend
                : GrnBackendKind.ManagedParser,
            WorldLightingMode = Enum.IsDefined(state.WorldLightingMode)
                ? state.WorldLightingMode
                : WorldLightingMode.TimedDayNightCycle,
            CharacterName = TestCharacters.GetDisplayName(TestCharacters.ResolveEntryId(state.CharacterName)),
            LastLocation = location
        };
    }

    private void RegisterScenes()
    {
        _scenes.Register(
            GameSceneId.InitialLoading,
            () => new InitialLoadingScene(
                _resourceLoader,
                _gameDirectory,
                () => _scenes.RequestSwitch(GameSceneId.GameLoading)),
            preserveInMemory: false);
        _scenes.Register(
            GameSceneId.GameLoading,
            CreateGameLoadingScene,
            preserveInMemory: false);
        _scenes.Register(
            GameSceneId.MainMenu,
            () => new PlaceholderScene(
                GameSceneId.MainMenu,
                "MAIN MENU",
                "MAIN MENU SUPPORT IS READY FOR ITS FUTURE CONTENT",
                _gameDirectory),
            preserveInMemory: true);
        _scenes.Register(
            GameSceneId.CharacterViewer,
            () => new PlaceholderScene(
                GameSceneId.CharacterViewer,
                "CHARACTER VIEWER",
                "CHARACTER VIEWER WILL BE ADDED LATER",
                _gameDirectory),
            preserveInMemory: true);
        _scenes.Register(
            GameSceneId.SaveSelector,
            () => new PlaceholderScene(
                GameSceneId.SaveSelector,
                "SAVE SELECTOR",
                "SAVE SELECTOR WILL BE ADDED LATER",
                _gameDirectory),
            preserveInMemory: true);
        _scenes.Register(
            GameSceneId.WorldMap,
            () => new WorldMapScene(
                _window,
                _gamepad,
                _scenes.RequestSwitch,
                destination => RuntimeScene.Teleport(destination),
                (textureName, cancellationToken) => RuntimeScene.LoadTextureAsync(textureName, cancellationToken),
                () => RuntimeScene.PlayerWorldPosition,
                () => RuntimeScene.GetMapAnnotations(_gameDirectory),
                _worldMapControls,
                _gameDirectory),
            preserveInMemory: true);
    }

    private InGameScene InitializeRuntime()
    {
        var resources = _resourceLoader.TransferToRuntime();
        try
        {
            if (_inGameScene is not null)
            {
                _renderer.ResetWorld();
                _inGameScene.Dispose();
            }
            _renderer.InitializeWorld(resources.Assets, resources.WorldArchive);
            _renderer.SetSkinningMode(_campaignLoadState.SkinningMode);
            var scene = new InGameScene(
                resources,
                _renderer,
                _window,
                _gamepad,
                _scenes.RequestSwitch,
                () =>
                {
                },
                _campaignLoadState);
            _scenes.ReplaceInactiveInstance(scene);
            _inGameScene = scene;
            _campaignReloadPending = false;
            return scene;
        }
        catch
        {
            resources.WorldArchive.Dispose();
            resources.Assets.Dispose();
            throw;
        }
    }

    private InGameScene RuntimeScene =>
        _inGameScene ?? throw new InvalidOperationException("The in-game scene has not been initialized.");

    private void ExecuteCheat(CheatCommand command)
    {
        switch (command)
        {
            case HelpCheatCommand:
                EngineLog.WriteLine("Map: set map <ancaria|underworld|close|status>; set map-names <on|off>; set map-npcs <on|off>; set map-fit now.");
                EngineLog.WriteLine("Campaigns: set campaign list; set campaign status; set campaign <bin-directory-name|absolute-path> reloads the world using that script set (default NetScriptCamp).");
                EngineLog.WriteLine("Viewport cheats: set window-size <width>x<height> resizes without activation; set viewport show reports client/output/scene dimensions. Timing cheats: set frame-log <on|off> reports frame pacing; set animation-log <on|off> reports CPU animation stages, upload bytes and completed GPU model/shadow timestamps every two seconds.");
                EngineLog.WriteLine("GPU preparation cheat: set skin-preparation <on|off> prepares resources independently of model skinning selection.");
                EngineLog.WriteLine("Door diagnostics: set door-list show lists decoded loaded playback positions and clip availability.");
                EngineLog.WriteLine("World focus: F / gamepad A interacts; set focus <status|list|clear|interact|assist|stop|walk:x,y|cone:x,y|mouse:x,y|pointer:x,y>; set render-time <seconds|live> freezes/resumes shader animation for screenshots.");
                EngineLog.WriteLine("Pose processing: set gpu-pose <on|off> opts independent/world models into GPU sampling/hierarchy/palettes; set gpu-pose-status show reports remaining CPU consumers. Default off; visibility is fail-open until GPU bounds are implemented.");
                EngineLog.WriteLine("World particle emission: set world-gpu-emission <on|off> selects GPU birth/RNG/lifetime ownership for script/sector objects; set particle-stats show reports readiness and memory; set world-particle-step <0..0.1|off> controls fixed-step validation. Default off; switching off explicitly snapshots current state.");
                EngineLog.WriteLine("Model skinning: set skinning <auto|cpu|gpu|gpuonly> (default Auto); set gpu-skinning <on|off> aliases Gpu/Cpu. GpuOnly skips unavailable draws; set gpu-only <on|off> controls both skinning and particles. set skin-crowd <0..128> adds independent asset-driven poses; set skin-stats show reports resource bytes; set shader-reload now; set quit now.");
                EngineLog.WriteLine("Particle cheats: set player-panel <on|off>; set particle-panel <on|off|play|toggles>; set particle-list <all|filter>; set particle-target <self|x,y>; set particle-follow <on|off>; set particle-play <FX name>; set particle-enable <FX name>; set particle-disable <FX name>; set particle-model <on|off>; set particle-stop all; set particle-crowd <0..64> creates asset-driven stress previews.");
                EngineLog.WriteLine("Cheats: teleport <x> <y>; noclip [on|off]; screenshot [label]; inspect <x> <y> [label]; traceelevation <bellevue-a|bellevue-b|shaddar>; set overlays <on|off>; set debug-panel <on|off>; set lighting <day|night|cycle|black>; set stairs <on|off>; set blocked <on|off>; set tessellation <on|off>; set particles <on|off>; set particle-simulation <Auto|CpuSimd|CpuScalar|Gpu|GpuOnly>; set particle-stats show; set item-flags <hex>; set character next; set facing <degrees>; set path <x>,<y>; set door toggle; set door-list show; set hdr <on|off>; set hdr-brightness <nits|os>; set hdr-scene-format <packed|fp16>; set hdr-unlit-multiplier <0-4>; set hdr-particle-multiplier <0-4>; set pacing <vrr|vsync|limit|manual>; set fps <30-1000>; set latency <off|on|boost>; set resolution <percentage|auto>; set autoscale <on|off>; set scaling <none|bilinear|fsr1|fsr2|fsr1motionadaptive>; set granny <managed|native>.");
                EngineLog.WriteLine("Footprints: set footprints <on|off|clear|status>; set move <x,y|stop|status> moves through the normal camera/collision path without simulated input.");
                EngineLog.WriteLine("Portals: set portals status lists default-open script billboards, triggers and destinations.");
                EngineLog.WriteLine("World transitions: set transitions status lists nearby script stairs/teleports and the current surface level.");
                return;
            case TeleportCheatCommand teleport:
                if (_inGameScene is null)
                {
                    EngineLog.WriteLine("Cheat: teleport is available once the in-game scene has loaded.");
                    return;
                }

                _inGameScene.Teleport(teleport.Position);
                EngineLog.WriteLine($"Cheat: teleported to {teleport.Position.X:0.##}, {teleport.Position.Y:0.##}.");
                return;
            case NoClipCheatCommand noClip:
                if (_inGameScene is null)
                {
                    EngineLog.WriteLine("Cheat: noclip is available once the in-game scene has loaded.");
                    return;
                }

                var noClipEnabled = noClip.Enabled ??
                    _inGameScene.CollisionMode != CollisionCheatMode.NoClip;
                _inGameScene.SetNoClipEnabled(noClipEnabled);
                EngineLog.WriteLine($"Cheat: noclip {(noClipEnabled ? "enabled" : "disabled")}.");
                return;
            case ScreenshotCheatCommand screenshot:
                CaptureScreenshot(screenshot.Label);
                return;
            case InspectionCheatCommand inspection:
                if (_inGameScene is null)
                {
                    EngineLog.WriteLine("Cheat: inspect is available once the in-game scene has loaded.");
                    return;
                }

                _inGameScene.Teleport(inspection.Position);
                _pendingInspection = new PendingInspection(
                    inspection.Position,
                    inspection.Label ?? $"inspect-{inspection.Position.X:0.##}-{inspection.Position.Y:0.##}",
                    DateTime.UtcNow + TimeSpan.FromSeconds(2));
                EngineLog.WriteLine(
                    $"Cheat: inspection queued at {inspection.Position.X:0.##}, {inspection.Position.Y:0.##}.");
                return;
            case ElevationTraceCheatCommand elevationTrace:
                if (_inGameScene is null)
                {
                    EngineLog.WriteLine("Cheat: elevation tracing is available once the in-game scene has loaded.");
                    return;
                }

                _inGameScene.TryStartElevationTrace(elevationTrace.Route, out var traceMessage);
                EngineLog.WriteLine($"Cheat: {traceMessage}");
                return;
            case SetOptionCheatCommand setOption:
                ExecuteSetOptionCheat(setOption);
                return;
            case InvalidCheatCommand invalid:
                EngineLog.WriteLine($"Cheat: {invalid.Message}");
                return;
        }
    }

    private void ExecuteSetOptionCheat(SetOptionCheatCommand command)
    {
        if (TrySetEngineCheatOption(command.Option, command.Value, out var engineMessage) ||
            command.Option.Equals("campaign", StringComparison.OrdinalIgnoreCase))
        {
            EngineLog.WriteLine($"Cheat: {engineMessage}");
            return;
        }

        if (_inGameScene is not null)
        {
            var applied = _inGameScene.TrySetCheatOption(command.Option, command.Value, out var sceneMessage);
            if (applied)
            {
            }

            EngineLog.WriteLine($"Cheat: {sceneMessage}");
            return;
        }

        EngineLog.WriteLine($"Cheat: {engineMessage}");
    }

    private void CaptureScreenshot(string? label)
    {
        try
        {
            _renderer.QueueScreenshot(label);
            EngineLog.WriteLine("Screenshot queued for the next rendered frame.");
        }
        catch (Exception exception)
        {
            EngineLog.WriteLine($"Screenshot failed: {exception.Message}");
        }
    }

    private void UpdatePendingInspection()
    {
        if (_pendingInspection is not { } inspection || _inGameScene is null)
            return;

        // Keep the gameplay anchor exact while sector streaming and composition settle.
        _inGameScene.Teleport(inspection.Position);
        if (DateTime.UtcNow < inspection.CaptureAfter || !_inGameScene.WorldStreamingSettled)
            return;

        _pendingInspection = null;
        CaptureScreenshot(inspection.Label);
    }

    private bool TrySetEngineCheatOption(string option, string value, out string message)
    {
        switch (option.ToLowerInvariant())
        {
            case "map-fit" when _scenes.ActiveScene is WorldMapScene mapScene:
                mapScene.FitMap();
                message = "map fitted to viewport";
                return true;
            case "map":
                return TrySetWorldMap(value, out message);
            case "map-names" when TryParseBoolean(value, out var mapNames):
                _worldMapControls.RegionNamesVisible = mapNames;
                message = $"map region names {(mapNames ? "visible" : "hidden")}";
                return true;
            case "map-npcs" when TryParseBoolean(value, out var mapNpcs):
                _worldMapControls.RegionNpcsVisible = mapNpcs;
                message = $"map region NPCs {(mapNpcs ? "visible" : "hidden")}";
                return true;
            case "campaign":
                return TrySetCampaign(value, out message);
            case "particle-panel" when value is "play" or "toggles":
                _debugUiControls.PlayerPanelVisible = true;
                _debugUiControls.RequestedParticlePanelOpen = true;
                _debugUiControls.RequestedParticlePreviewMode = value == "play";
                message = $"particle panel showing {value}";
                return true;
            case "player-panel" when TryParseBoolean(value, out var playerPanel):
                _debugUiControls.PlayerPanelVisible = playerPanel;
                message = $"player panel {(playerPanel ? "visible" : "hidden")}";
                return true;
            case "particle-panel" when TryParseBoolean(value, out var particlePanel):
                _debugUiControls.PlayerPanelVisible = true;
                _debugUiControls.RequestedParticlePanelOpen = particlePanel;
                message = $"particle panel {(particlePanel ? "opened" : "closed")}";
                return true;
            case "hdr" when TryParseBoolean(value, out var hdrEnabled):
                if (_renderer.IsHdrEnabled != hdrEnabled)
                    _renderer.ToggleHdr();
                message = $"HDR {(hdrEnabled ? "enabled" : "disabled")}";
                return true;
            case "hdr-brightness" when float.TryParse(value, out var frameWhiteNits) && float.IsFinite(frameWhiteNits):
                _renderer.SetHdrBrightness(frameWhiteNits);
                message = $"HDR frame white {_renderer.HdrBrightnessSettings.SceneBrightnessNits:0.##} nits; native effect intensity";
                return true;
            case "hdr-brightness" when value.Equals("os", StringComparison.OrdinalIgnoreCase):
                message = _renderer.ResetHdrBrightnessToOs()
                    ? $"HDR frame white {_renderer.HdrBrightnessSettings.SceneBrightnessNits:0.##} nits from OS SDR white"
                    : "OS SDR white unavailable; frame brightness unchanged";
                return true;
            case "hdr-scene-format" when value.Equals("packed", StringComparison.OrdinalIgnoreCase) ||
                                        value.Equals("fp16", StringComparison.OrdinalIgnoreCase):
                _renderer.SetPackedHdrScene(value.Equals("packed", StringComparison.OrdinalIgnoreCase));
                message = $"HDR scene format {(_renderer.UsesPackedHdrScene ? "packed" : "fp16")}";
                return true;
            case "hdr-unlit-multiplier" when float.TryParse(value, out var unlitMultiplier) && float.IsFinite(unlitMultiplier):
                _renderer.SetHdrUnlitColorMultiplier(unlitMultiplier);
                message = $"HDR unlit objects color multiplier {_renderer.HdrBrightnessSettings.UnlitColorMultiplier:0.##}x";
                return true;
            case "hdr-particle-multiplier" when float.TryParse(value, out var particleMultiplier) && float.IsFinite(particleMultiplier):
                _renderer.SetHdrParticleColorMultiplier(particleMultiplier);
                message = $"HDR particles color multiplier {_renderer.HdrBrightnessSettings.ParticleColorMultiplier:0.##}x";
                return true;
            case "frame-log" when TryParseBoolean(value, out var frameLog):
                _renderer.FrameTiming.Enabled = frameLog;
                message = $"frame timing log {(frameLog ? "enabled" : "disabled")}";
                return true;
            case "animation-log" when TryParseBoolean(value, out var animationLog):
                _renderer.FrameTiming.Animation.Reset();
                AnimationPerformance.Enabled = animationLog;
                ParticlePerformance.Enabled = animationLog;
                message = $"animation timing log {(animationLog ? "enabled" : "disabled")}";
                return true;
            case "skin-preparation" when TryParseBoolean(value, out var skinPreparation):
                if (!_renderer.WorldInitialized) { message = "skin preparation requires a loaded world"; return false; }
                _renderer.SetSkinPreparation(skinPreparation);
                message = $"GPU skin preparation {(skinPreparation ? "enabled" : "disabled")}; independent of model skinning selection";
                return true;
            case "window-size":
                var dimensions = value.Split('x', StringSplitOptions.TrimEntries);
                if (dimensions.Length != 2 || !int.TryParse(dimensions[0], out var windowWidth) ||
                    !int.TryParse(dimensions[1], out var windowHeight) ||
                    windowWidth is < 320 or > 16384 || windowHeight is < 320 or > 16384 ||
                    _window.IsBorderlessFullscreen || _window.WindowedMaximized)
                {
                    message = "window-size requires <width>x<height> (320..16384) in a restored window";
                    return false;
                }
                _window.SetWindowedSize(windowWidth, windowHeight);
                message = $"window resized to {windowWidth}x{windowHeight}";
                return true;
            case "viewport":
                message = $"viewport client {_window.ClientWidth}x{_window.ClientHeight}, output {_renderer.OutputWidth}x{_renderer.OutputHeight}, scene {_renderer.RenderWidth}x{_renderer.RenderHeight}, resolution {_renderer.RenderResolutionPercentage * 100:0.##}%, auto {_renderer.AutoRenderResolution}";
                return true;
            case "quit":
                _window.RequestQuit();
                message = "shutdown requested";
                return true;
            case "particle-stats":
                message = _renderer.ParticleGpuStatus;
                return true;
            case "skin-stats":
                message = _renderer.SkinningStatus;
                return true;
            case "shader-reload":
                _renderer.ReloadShaders();
                message = "shader reload requested";
                return true;
            case "skinning" when Enum.TryParse<SkinningMode>(value, true, out var skinningMode) && Enum.IsDefined(skinningMode):
                if (!_renderer.WorldInitialized) { message = "model skinning requires a loaded world"; return false; }
                _renderer.SetSkinningMode(skinningMode);
                message = $"model skinning set to {skinningMode}; " +
                    (skinningMode == SkinningMode.GpuOnly ? "CPU fallback disabled; unavailable GPU draws skipped" : "CPU fallback allowed");
                return true;
            case "gpu-only" when TryParseBoolean(value, out var gpuOnly):
                if (!_renderer.WorldInitialized || _inGameScene is null) { message = "GPU-only requires a loaded world"; return false; }
                _renderer.SetSkinningMode(gpuOnly ? SkinningMode.GpuOnly : SkinningMode.Auto);
                _inGameScene.SetParticleSimulation(gpuOnly ? ParticleSimulationMode.GpuOnly : ParticleSimulationMode.Auto);
                message = gpuOnly ? "GPU-only enabled; CPU skinning/simulation fallbacks disabled; pose sampling, particle births and lifetime accounting remain on CPU" : "GPU-only disabled; automatic GPU selection with CPU fallback restored";
                return true;
            case "gpu-skinning" when TryParseBoolean(value, out var gpuSkinning):
                if (!_renderer.WorldInitialized) { message = "GPU skinning requires a loaded world"; return false; }
                _renderer.SetGpuSkinning(gpuSkinning);
                message = gpuSkinning ? "GPU skeletal drawing requested; pose-only playback when ready" : "CPU skeletal drawing requested; current poses prepared before drawing";
                return true;
            case "gpu-pose" when TryParseBoolean(value, out var gpuPose):
                if (!_renderer.WorldInitialized) { message = "GPU pose playback requires a loaded world"; return false; }
                _renderer.SetGpuPosePlayback(gpuPose);
                message = gpuPose ? "GPU pose playback requested for independent/world models; remaining CPU consumers are reported by gpu-pose-status" : "CPU pose playback restored at the current requested sample";
                return true;
            case "gpu-pose-status":
                message = _renderer.GpuPoseStatus;
                return true;
            case "world-gpu-emission" when TryParseBoolean(value, out var worldEmission):
                if (_inGameScene is null || !_renderer.WorldInitialized) { message = "World emission requires a loaded world"; return false; }
                _inGameScene.SetWorldEmission(worldEmission);
                message = worldEmission ? "GPU-owned world-object emission enabled; previews/equipment retain their selected backends" : "World-object emission explicitly restored to its previous simulation backend";
                return true;
            case "pacing" when TryParseFramePacing(value, out var pacingMode):
                _framePacing.SetMode(pacingMode);
                message = $"frame pacing set to {_framePacing.Status}";
                return true;
            case "fps" when int.TryParse(value, out var manualFrameRate):
                _framePacing.SetManualFrameRate(manualFrameRate);
                message = $"manual frame rate set to {_framePacing.ManualFrameRate} FPS";
                return true;
            case "latency" when TryParseLowLatencyMode(value, out var latencyMode):
                _framePacing.SetLowLatencyMode(latencyMode);
                message = $"low latency set to {latencyMode}";
                return true;
            case "granny" when TryParseGrannyBackend(value, out var grannyBackend):
                _grannyBackend = grannyBackend;
                message = $"Granny implementation set to {FormatGrannyBackend(grannyBackend)}; restart to reload game assets";
                return true;
            case "resolution" or "res" when value.Equals("auto", StringComparison.OrdinalIgnoreCase):
                _renderer.SetAutoRenderResolution(true);
                message = "render resolution set to auto (1:1 tiles)";
                return true;
            case "resolution" or "res" when int.TryParse(value, out var resolutionPercentage):
                _renderer.SetRenderResolutionPercentage(Math.Clamp(resolutionPercentage, 25, 200));
                message = $"render resolution set to {_renderer.RenderResolutionPercentage * 100:0.##}%";
                return true;
            case "autoscale" or "autoresolution" or "auto-resolution" when TryParseBoolean(value, out var autoScale):
                _renderer.SetAutoRenderResolution(autoScale);
                message = $"auto resolution scaling {(autoScale ? "enabled (1:1 tiles)" : "disabled")}";
                return true;
            case "scaling" when Enum.TryParse<RenderScalingMode>(value, ignoreCase: true, out var scalingMode):
                _renderer.SetRenderScalingMode(scalingMode);
                message = $"render scaling mode set to {scalingMode}";
                return true;
            default:
                message = "Unknown option. Type 'help' for commands.";
                return false;
        }
    }

    private static int NormalizeWindowDimension(int dimension, int fallback) =>
        dimension is >= 320 and <= 16_384 ? dimension : fallback;

    private static float NormalizePlayerMovementSpeedMultiplier(float value) =>
        float.IsFinite(value) ? Math.Clamp(value, 0.25f, 4.0f) : 1.0f;

    private static bool TryParseBoolean(string value, out bool enabled)
    {
        enabled = value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
                  value.Equals("true", StringComparison.OrdinalIgnoreCase);
        return enabled || value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("false", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseFramePacing(string value, out FramePacingMode mode)
    {
        mode = value.ToLowerInvariant() switch
        {
            "vrr" => FramePacingMode.VariableRefreshRate,
            "vsync" => FramePacingMode.VSync,
            "limit" or "limiter" => FramePacingMode.MonitorRefreshLimiter,
            "manual" => FramePacingMode.Manual,
            _ => default
        };
        return value.Equals("vrr", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("vsync", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("limit", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("limiter", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("manual", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseLowLatencyMode(string value, out LowLatencyMode mode)
    {
        mode = value.ToLowerInvariant() switch
        {
            "off" => LowLatencyMode.Off,
            "on" => LowLatencyMode.On,
            "boost" or "onplusboost" => LowLatencyMode.OnPlusBoost,
            _ => default
        };
        return value.Equals("off", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("boost", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("onplusboost", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseGrannyBackend(string value, out GrnBackendKind backend)
    {
        backend = value.ToLowerInvariant() switch
        {
            "native" or "dll" or "granny.dll" or "granny_x64.dll" => GrnBackendKind.GrannyDll,
            "managed" or "parser" => GrnBackendKind.ManagedParser,
            _ => default
        };
        return value.Equals("native", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("dll", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("granny.dll", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("granny_x64.dll", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("managed", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("parser", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatGrannyBackend(GrnBackendKind backend) => backend switch
    {
        GrnBackendKind.GrannyDll => "granny_x64.dll",
        _ => "Managed"
    };

    private sealed record PendingInspection(Vector2 Position, string Label, DateTime CaptureAfter);
}
