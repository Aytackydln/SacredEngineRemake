using System;
using System.IO;
using System.Numerics;
using Sacred.Core;
using Sacred.Engine.Assets;
using Sacred.Engine.Cheats;
using Sacred.Engine.Graphics;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny.Abstractions;
using Sacred.Particles;

namespace Sacred.Engine;

/// <summary>Owns scene construction, engine-wide input, cheats, and persistent runtime state.</summary>
internal sealed class SacredGameRuntime : IDisposable
{
    private readonly Win32Window _window;
    private readonly LowLatencySystem _latency;
    private readonly Dx12Renderer _renderer;
    private readonly FramePacingController _framePacing;
    private readonly SceneManager _scenes;
    private readonly GameResourceLoader _resourceLoader;
    private readonly GamepadInputSource _gamepad = new();
    private readonly EngineInputController _engineInput;
    private readonly CheatsController _cheats;
    private readonly string _gameDirectory;
    private readonly SacredGameSaveState _initialSaveState;
    private readonly DebugUiControlState _debugUiControls;

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
        _grannyBackend = initialSaveState.GrannyBackend;
        _gameDirectory = gameDirectory;
        _window = window;
        _latency = latency;
        _renderer = renderer;
        _debugUiControls = renderer.DebugUiControls;
        _framePacing = framePacing;
        _scenes = scenes;
        _resourceLoader = new GameResourceLoader(gameDirectories, _grannyBackend);
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
        ApplyDebugUiRequests();
        _gamepad.Poll(_window.Input);
        _cheats.Update(ExecuteCheat);
        if (_window.Input.ConsumePressed(VirtualKey.F12))
            CaptureScreenshot(null);
        _engineInput.Update();
        _scenes.Update(deltaSeconds);
        SynchronizeDebugUiControls();
        UpdatePendingInspection();
    }

    private void ApplyDebugUiRequests()
    {
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

        if (_debugUiControls.ScreenshotRequested)
        {
            _debugUiControls.ScreenshotRequested = false;
            CaptureScreenshot(null);
        }
    }

    private void SynchronizeDebugUiControls()
    {
        _debugUiControls.HdrEnabled = _renderer.IsHdrEnabled;
        _debugUiControls.FramePacingMode = _framePacing.Mode;
        _debugUiControls.LowLatencyMode = _latency.Mode;
        _debugUiControls.WorldLightingMode =
            _inGameScene?.WorldLightingMode ?? _initialSaveState.WorldLightingMode;
        _debugUiControls.BorderlessFullscreen = _window.IsBorderlessFullscreen;
        _debugUiControls.ParticleQuality = _inGameScene?.ParticleQuality ?? _initialSaveState.ParticleQuality;
        _debugUiControls.CollisionMode = _inGameScene?.CollisionMode ?? CollisionCheatMode.Walk;
        _debugUiControls.PlayerMovementSpeedMultiplier =
            _inGameScene?.PlayerMovementSpeedMultiplier ?? _initialSaveState.PlayerMovementSpeedMultiplier;
        _debugUiControls.RenderResolutionPercentage = _renderer.RenderResolutionPercentage;
        _debugUiControls.AutoRenderResolution = _renderer.AutoRenderResolution;
        _debugUiControls.RenderScalingMode = _renderer.RenderScalingMode;
        _debugUiControls.Player = _inGameScene?.CreatePlayerDebugPanelState();
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
            LowLatencyMode = _latency.Mode,
            RenderResolutionPercentage = _renderer.RenderResolutionPercentage,
            AutoRenderResolution = _renderer.AutoRenderResolution,
            RenderScalingMode = _renderer.RenderScalingMode,
            GrannyBackend = _grannyBackend,
            WorldLightingMode = _inGameScene?.WorldLightingMode ?? _initialSaveState.WorldLightingMode,
            StairsTilesVisible = _inGameScene?.StairsTilesVisible ?? _initialSaveState.StairsTilesVisible,
            BlockedTilesVisible = _inGameScene?.BlockedTilesVisible ?? _initialSaveState.BlockedTilesVisible,
            PlayerMovementSpeedMultiplier =
                _inGameScene?.PlayerMovementSpeedMultiplier ?? _initialSaveState.PlayerMovementSpeedMultiplier,
            ParticleQuality = _inGameScene?.ParticleQuality ?? _initialSaveState.ParticleQuality,
            CharacterName = _inGameScene?.SelectedCharacterName ?? _initialSaveState.CharacterName,
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
            HdrBrightness = (state.HdrBrightness ?? HdrBrightnessSettings.Default).Normalized(),
            FramePacingMode = Enum.IsDefined(state.FramePacingMode)
                ? state.FramePacingMode
                : FramePacingMode.VariableRefreshRate,
            LowLatencyMode = Enum.IsDefined(state.LowLatencyMode)
                ? state.LowLatencyMode
                : LowLatencyMode.On,
            RenderResolutionPercentage = Math.Clamp(state.RenderResolutionPercentage, 25, 200),
            AutoRenderResolution = state.AutoRenderResolution,
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
            () => new GameLoadingScene(
                _resourceLoader,
                _gameDirectory,
                InitializeRuntime,
                _scenes.RequestSwitch),
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
                _gameDirectory),
            preserveInMemory: true);
    }

    private InGameScene InitializeRuntime()
    {
        var resources = _resourceLoader.TransferToRuntime();
        try
        {
            _renderer.InitializeWorld(resources.Assets, resources.WorldArchive);
            var scene = new InGameScene(
                resources,
                _renderer,
                _window,
                _gamepad,
                _scenes.RequestSwitch,
                () =>
                {
                },
                _initialSaveState);
            _scenes.RegisterInstance(scene);
            _inGameScene = scene;
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
                EngineLog.WriteLine("Cheats: teleport <x> <y>; noclip [on|off]; screenshot [label]; inspect <x> <y> [label]; traceelevation <bellevue-a|bellevue-b|shaddar>; set overlays <on|off>; set debug-panel <on|off>; set lighting <day|night|cycle|black>; set stairs <on|off>; set blocked <on|off>; set tessellation <on|off>; set particles <on|off>; set item-flags <hex>; set character next; set hdr <on|off>; set pacing <vrr|vsync|limit>; set latency <off|on|boost>; set resolution <percentage|auto>; set autoscale <on|off>; set scaling <none|bilinear|fsr1>; set granny <managed|native>.");
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
        if (TrySetEngineCheatOption(command.Option, command.Value, out var engineMessage))
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
            case "hdr" when TryParseBoolean(value, out var hdrEnabled):
                if (_renderer.IsHdrEnabled != hdrEnabled)
                    _renderer.ToggleHdr();
                message = $"HDR {(hdrEnabled ? "enabled" : "disabled")}";
                return true;
            case "pacing" when TryParseFramePacing(value, out var pacingMode):
                _framePacing.SetMode(pacingMode);
                message = $"frame pacing set to {_framePacing.Status}";
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
                message = $"render resolution set to {_renderer.RenderResolutionPercentage}%";
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
            _ => default
        };
        return value.Equals("vrr", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("vsync", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("limit", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("limiter", StringComparison.OrdinalIgnoreCase);
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
