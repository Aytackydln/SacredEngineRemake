using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core;
using Sacred.Engine.Assets;
using Sacred.Engine.Cheats;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny.Abstractions;

namespace Sacred.Engine;

/// <summary>Owns engine lifetime and coordinates the active scene's frame stages.</summary>
public sealed class SacredGame : IDisposable
{
    private readonly Win32Window _window;
    private readonly LowLatencySystem _latency;
    private readonly Dx12Renderer _renderer;
    private readonly GameResourceLoader _resourceLoader;
    private readonly SceneManager _scenes = new();
    private readonly GamepadInputSource _gamepad = new();
    private readonly EngineInputController _engineInput;
    private readonly CheatsController _cheats;
    private readonly uint _displayRefreshRateHz;
    private readonly string _gameDirectory;
    private readonly SacredGameSaveState _initialSaveState;

    private InGameScene? _inGameScene;
    private FramePacingMode _mode;
    private GrnBackendKind _grannyBackend;
    private string _framePacingStatus = string.Empty;
    private bool _disposed;

    public SacredGame(SacredGameDirectories gameDirectories, SacredGameSaveState? saveState = null)
    {
        ArgumentNullException.ThrowIfNull(gameDirectories);
        _initialSaveState = NormalizeSaveState(saveState ?? new SacredGameSaveState());
        _mode = _initialSaveState.FramePacingMode;
        _grannyBackend = _initialSaveState.GrannyBackend;
        _gameDirectory = ResolveGameDirectory(gameDirectories);
        _latency = LowLatencySystem.CreateDefault();
        _window = new Win32Window(
            "Sacred Remake",
            _initialSaveState.WindowedWidth,
            _initialSaveState.WindowedHeight,
            _initialSaveState.BorderlessFullscreen);
        _displayRefreshRateHz = _window.DisplayRefreshRateHz;
        _renderer = new Dx12Renderer(_window, _gameDirectory, _latency, _initialSaveState.HdrEnabled);
        _latency.SetMode(_initialSaveState.LowLatencyMode, 0);
        _resourceLoader = new GameResourceLoader(gameDirectories, _grannyBackend);
        _engineInput = new EngineInputController(
            _window.Input,
            _renderer,
            _latency,
            CycleFramePacing,
            _window.ToggleBorderlessFullscreen,
            UpdateWindowTitle);
        _cheats = new CheatsController(Console.In);

        RegisterScenes();
        _scenes.SceneChanged += UpdateWindowTitle;
        _scenes.Start(GameSceneId.InitialLoading);
        _window.RequestFocus();
    }

    public Task Run(CancellationToken cancellationToken = default) =>
        Win32AsyncPump.RunAsync(() => RunCoreAsync(cancellationToken), _window.ProcessMessages);

    public SacredGameSaveState CaptureSaveState() => new()
    {
        BorderlessFullscreen = _window.IsBorderlessFullscreen,
        WindowedWidth = _window.WindowedWidth,
        WindowedHeight = _window.WindowedHeight,
        HdrEnabled = _renderer.IsHdrEnabled,
        FramePacingMode = _mode,
        LowLatencyMode = _latency.Mode,
        GrannyBackend = _grannyBackend,
        WorldLightingMode = _inGameScene?.WorldLightingMode ?? _initialSaveState.WorldLightingMode,
        StairsTilesVisible = _inGameScene?.StairsTilesVisible ?? _initialSaveState.StairsTilesVisible,
        BlockedTilesVisible = _inGameScene?.BlockedTilesVisible ?? _initialSaveState.BlockedTilesVisible,
        CharacterName = _inGameScene?.SelectedCharacterName ?? _initialSaveState.CharacterName,
        LastLocation = _inGameScene?.PlayerWorldPosition ?? _initialSaveState.LastLocation
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _scenes.SceneChanged -= UpdateWindowTitle;
        // GPU caches can reference scene-owned assets, so retire them before disposing scenes.
        _renderer.Dispose();
        _cheats.Dispose();
        _scenes.Dispose();
        _resourceLoader.Dispose();
        _latency.Dispose();
        _window.Dispose();
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
                UpdateWindowTitle,
                _initialSaveState);
            _scenes.RegisterInstance(scene);
            _inGameScene = scene;
            UpdateWindowTitle();
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

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        var clock = new FrameClock(_displayRefreshRateHz);
        var frameId = 0UL;

        while (!cancellationToken.IsCancellationRequested)
        {
            await clock.WaitForFrameStartAsync(_mode, cancellationToken);
            _latency.SetMode(_latency.Mode, _mode == FramePacingMode.VSync ? 0 : clock.TargetFrameRate);

            _latency.BeginFrame(frameId);
            _latency.SleepBeforeInput(frameId);

            if (!_window.ProcessMessages())
                break;

            var deltaSeconds = clock.Tick();
            _latency.Mark(LatencyMarker.SimulationStart, frameId);
            if (_window.Input.HasPendingLeftClick)
                _latency.Mark(LatencyMarker.LeftMouseButtonClick, frameId);

            _gamepad.Poll(_window.Input);
            _cheats.Update(ExecuteCheat);
            _engineInput.Update();
            _scenes.Update(deltaSeconds);
            _latency.Mark(LatencyMarker.SimulationEnd, frameId);

            await _scenes.ActiveScene.RenderAsync(new SceneRenderContext(
                _renderer,
                ShouldPresentWithVSync(),
                _framePacingStatus,
                frameId,
                cancellationToken));
            frameId++;
        }
    }

    private bool ShouldPresentWithVSync() =>
        _mode == FramePacingMode.VSync ||
        (_mode == FramePacingMode.VariableRefreshRate && !_renderer.VariableRefreshRateSupported);

    private void CycleFramePacing() => _mode = NextFramePacingMode(_mode);

    private void ExecuteCheat(CheatCommand command)
    {
        switch (command)
        {
            case HelpCheatCommand:
                Console.WriteLine("Cheats: teleport <x> <y>; set overlays <on|off>; set lighting <day|night|cycle|black>; set stairs <on|off>; set blocked <on|off>; set character next; set hdr <on|off>; set pacing <vrr|vsync|limit>; set latency <off|on|boost>; set granny <managed|native>.");
                return;
            case TeleportCheatCommand teleport:
                if (_inGameScene is null)
                {
                    Console.WriteLine("Cheat: teleport is available once the in-game scene has loaded.");
                    return;
                }

                _inGameScene.Teleport(teleport.Position);
                Console.WriteLine($"Cheat: teleported to {teleport.Position.X:0.##}, {teleport.Position.Y:0.##}.");
                return;
            case SetOptionCheatCommand setOption:
                ExecuteSetOptionCheat(setOption);
                return;
            case InvalidCheatCommand invalid:
                Console.WriteLine($"Cheat: {invalid.Message}");
                return;
        }
    }

    private void ExecuteSetOptionCheat(SetOptionCheatCommand command)
    {
        if (TrySetEngineCheatOption(command.Option, command.Value, out var engineMessage))
        {
            Console.WriteLine($"Cheat: {engineMessage}");
            return;
        }

        if (_inGameScene is not null)
        {
            var applied = _inGameScene.TrySetCheatOption(command.Option, command.Value, out var sceneMessage);
            if (applied)
                UpdateWindowTitle();
            Console.WriteLine($"Cheat: {sceneMessage}");
            return;
        }

        Console.WriteLine($"Cheat: {engineMessage}");
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
                _mode = pacingMode;
                UpdateWindowTitle();
                message = $"frame pacing set to {FormatFramePacingMode()}";
                return true;
            case "latency" when TryParseLowLatencyMode(value, out var latencyMode):
                _latency.SetMode(latencyMode, _mode == FramePacingMode.VSync ? 0 : _displayRefreshRateHz);
                UpdateWindowTitle();
                message = $"low latency set to {latencyMode}";
                return true;
            case "granny" when TryParseGrannyBackend(value, out var grannyBackend):
                _grannyBackend = grannyBackend;
                UpdateWindowTitle();
                message = $"Granny implementation set to {FormatGrannyBackend(grannyBackend)}; restart to reload game assets";
                return true;
            default:
                message = "Unknown option. Type 'help' for commands.";
                return false;
        }
    }

    private static FramePacingMode NextFramePacingMode(FramePacingMode mode) => mode switch
    {
        FramePacingMode.VariableRefreshRate => FramePacingMode.VSync,
        FramePacingMode.VSync => FramePacingMode.MonitorRefreshLimiter,
        _ => FramePacingMode.VariableRefreshRate
    };

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
            "native" or "dll" or "granny.dll" => GrnBackendKind.GrannyDll,
            "managed" or "parser" => GrnBackendKind.ManagedParser,
            _ => default
        };
        return value.Equals("native", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("dll", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("granny.dll", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("managed", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("parser", StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveGameDirectory(SacredGameDirectories gameDirectories)
    {
        var pakDirectory = Path.GetDirectoryName(gameDirectories.TexturesPakPath);
        return Path.GetDirectoryName(pakDirectory) ?? ".";
    }

    private static SacredGameSaveState NormalizeSaveState(SacredGameSaveState state)
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
            FramePacingMode = Enum.IsDefined(state.FramePacingMode)
                ? state.FramePacingMode
                : FramePacingMode.VariableRefreshRate,
            LowLatencyMode = Enum.IsDefined(state.LowLatencyMode)
                ? state.LowLatencyMode
                : LowLatencyMode.On,
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

    private static int NormalizeWindowDimension(int dimension, int fallback) =>
        dimension is >= 320 and <= 16_384 ? dimension : fallback;

    private void UpdateWindowTitle()
    {
        var lowLatencyMode = _latency.Mode switch
        {
            LowLatencyMode.OnPlusBoost => "On + Boost",
            LowLatencyMode.On => "On",
            _ => "Off"
        };

        _framePacingStatus = FormatFramePacingMode();
        var lighting = _inGameScene?.LightingDisplayName ?? "not active";
        var granny = _grannyBackend == _initialSaveState.GrannyBackend
            ? FormatGrannyBackend(_grannyBackend)
            : $"{FormatGrannyBackend(_initialSaveState.GrannyBackend)} (next: {FormatGrannyBackend(_grannyBackend)})";
        _window.SetTitle(
            $"SacredEngineRemake - Scene: {_scenes.ActiveSceneId} - Pacing: {_framePacingStatus} - Lighting: {lighting} - Granny: {granny} - Low Latency: {lowLatencyMode} ({_latency.ActiveBackendName})");
    }

    private static string FormatGrannyBackend(GrnBackendKind backend) => backend switch
    {
        GrnBackendKind.GrannyDll => "Granny.dll",
        _ => "Managed"
    };

    private string FormatFramePacingMode() => _mode switch
    {
        FramePacingMode.VariableRefreshRate => _renderer.VariableRefreshRateSupported
            ? $"VRR, {_displayRefreshRateHz} FPS cap"
            : "VRR unavailable, VSync fallback",
        FramePacingMode.VSync => "VSync",
        FramePacingMode.MonitorRefreshLimiter => $"{_displayRefreshRateHz} FPS limiter",
        _ => _mode.ToString()
    };
}
