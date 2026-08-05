using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;

namespace Sacred.Engine;

/// <summary>Owns engine lifetime and coordinates the active scene's frame stages.</summary>
public sealed class SacredGame : IDisposable
{
    private static FramePacingMode _mode = FramePacingMode.VariableRefreshRate;

    private readonly Win32Window _window;
    private readonly LowLatencySystem _latency;
    private readonly Dx12Renderer _renderer;
    private readonly GameResourceLoader _resourceLoader;
    private readonly SceneManager _scenes = new();
    private readonly GamepadInputSource _gamepad = new();
    private readonly EngineInputController _engineInput;
    private readonly uint _displayRefreshRateHz;
    private readonly string _gameDirectory;

    private InGameScene? _inGameScene;
    private string _framePacingStatus = string.Empty;
    private bool _disposed;

    public SacredGame(SacredGameDirectories gameDirectories)
    {
        ArgumentNullException.ThrowIfNull(gameDirectories);
        _gameDirectory = ResolveGameDirectory(gameDirectories);
        _latency = LowLatencySystem.CreateDefault();
        _window = new Win32Window("Sacred Remake", 1600, 900);
        _displayRefreshRateHz = _window.DisplayRefreshRateHz;
        _renderer = new Dx12Renderer(_window, _gameDirectory, _latency);
        _resourceLoader = new GameResourceLoader(gameDirectories);
        _engineInput = new EngineInputController(
            _window.Input,
            _renderer,
            _latency,
            CycleFramePacing,
            UpdateWindowTitle);

        RegisterScenes();
        _scenes.SceneChanged += UpdateWindowTitle;
        _scenes.Start(GameSceneId.InitialLoading);
        _window.RequestFocus();
    }

    public Task Run(CancellationToken cancellationToken = default) =>
        Win32AsyncPump.RunAsync(() => RunCoreAsync(cancellationToken), _window.ProcessMessages);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _scenes.SceneChanged -= UpdateWindowTitle;
        // GPU caches can reference scene-owned assets, so retire them before disposing scenes.
        _renderer.Dispose();
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
                _window.Input,
                _gamepad,
                _scenes.RequestSwitch,
                _gameDirectory),
            preserveInMemory: true);
    }

    private InGameScene InitializeRuntime()
    {
        var resources = _resourceLoader.TransferToRuntime();
        try
        {
            _renderer.InitializeWorld(resources.Assets);
            var scene = new InGameScene(
                resources,
                _renderer,
                _window,
                _gamepad,
                _scenes.RequestSwitch,
                UpdateWindowTitle);
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

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        var clock = new FrameClock(_displayRefreshRateHz);
        var frameId = 0UL;

        while (!cancellationToken.IsCancellationRequested)
        {
            await clock.WaitForFrameStartAsync(_mode, cancellationToken);
            _latency.SetMode(_latency.Mode, _mode == FramePacingMode.VSync ? 0 : clock.TargetFrameRate);

            frameId++;
            _latency.BeginFrame(frameId);
            _latency.SleepBeforeInput(frameId);

            if (!_window.ProcessMessages())
                break;

            var deltaSeconds = clock.Tick();
            _latency.Mark(LatencyMarker.SimulationStart, frameId);
            if (_window.Input.HasPendingLeftClick)
                _latency.Mark(LatencyMarker.LeftMouseButtonClick, frameId);

            _gamepad.Poll(_window.Input);
            _engineInput.Update();
            _scenes.Update(deltaSeconds);
            _latency.Mark(LatencyMarker.SimulationEnd, frameId);

            await _scenes.ActiveScene.RenderAsync(new SceneRenderContext(
                _renderer,
                ShouldPresentWithVSync(),
                _framePacingStatus,
                frameId,
                cancellationToken));
        }
    }

    private bool ShouldPresentWithVSync() =>
        _mode == FramePacingMode.VSync ||
        (_mode == FramePacingMode.VariableRefreshRate && !_renderer.VariableRefreshRateSupported);

    private void CycleFramePacing() => _mode = NextFramePacingMode(_mode);

    private static FramePacingMode NextFramePacingMode(FramePacingMode mode) => mode switch
    {
        FramePacingMode.VariableRefreshRate => FramePacingMode.VSync,
        FramePacingMode.VSync => FramePacingMode.MonitorRefreshLimiter,
        _ => FramePacingMode.VariableRefreshRate
    };

    private static string ResolveGameDirectory(SacredGameDirectories gameDirectories)
    {
        var pakDirectory = Path.GetDirectoryName(gameDirectories.TexturesPakPath);
        return Path.GetDirectoryName(pakDirectory) ?? ".";
    }

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
        _window.SetTitle(
            $"SacredEngineRemake - Scene: {_scenes.ActiveSceneId} - Pacing: {_framePacingStatus} - Lighting: {lighting} - Low Latency: {lowLatencyMode} ({_latency.ActiveBackendName})");
    }

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
