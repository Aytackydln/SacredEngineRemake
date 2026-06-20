using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Gaming.Input;
using Sacred.Core;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Scene;
using Sacred.Engine.World;

namespace Sacred.Engine;

/// <summary>Owns engine lifetime and coordinates the latency-sensitive frame stages.</summary>
public sealed class SacredGame : IDisposable
{
    private static FramePacingMode _mode = FramePacingMode.VariableRefreshRate;

    private readonly Win32Window _window;
    private readonly LowLatencySystem _latency;
    private readonly Dx12Renderer _renderer;
    private readonly AssetManager _assets;
    private readonly WorldStreamer _worldStreamer;
    private readonly SacredCamera _camera;
    private readonly ClickToMoveController _clickToMove = new();
    private readonly SceneState _scene = new();
    private readonly WorldLightingController _worldLighting = new();
    private readonly PlayerCharacterController _player;
    private readonly uint _displayRefreshRateHz;

    private GamepadButtons _previousGamepadButtons;
    private string _framePacingStatus = string.Empty;
    private bool _disposed;

    public SacredGame(SacredGameDirectories gameDirectories)
    {
        _latency = LowLatencySystem.CreateDefault();
        _window = new Win32Window("Sacred Remake DX12 Prototype", 1600, 900);
        _displayRefreshRateHz = _window.DisplayRefreshRateHz;
        _assets = new AssetManager(gameDirectories);
        _camera = SacredCamera.CreateDefault(_window.ClientWidth, _window.ClientHeight);
        _worldStreamer = new WorldStreamer(SacredWorldArchive.Load(gameDirectories));
        _renderer = new Dx12Renderer(_window, _assets, ResolveGameDirectory(gameDirectories), _latency);
        _player = new PlayerCharacterController(_assets, _scene);

        UpdateWindowTitle();
        BootstrapScene();
    }

    public Task Run(CancellationToken cancellationToken = default) =>
        Win32AsyncPump.RunAsync(() => RunCoreAsync(cancellationToken), _window.ProcessMessages);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _player.Dispose();
        _renderer.Dispose();
        _latency.Dispose();
        _worldStreamer.Dispose();
        _assets.Dispose();
        _window.Dispose();
    }

    private void BootstrapScene()
    {
        _worldStreamer.CenterOnSector(_worldStreamer.StartSector.X, _worldStreamer.StartSector.Y);
        _camera.CenterOnTile(
            _worldStreamer.StartSector.X * WorldStreamer.SectorTileCount + WorldStreamer.SectorTileCount * 0.5f,
            _worldStreamer.StartSector.Y * WorldStreamer.SectorTileCount + WorldStreamer.SectorTileCount * 0.5f,
            0.75f);
        _scene.Lighting.LightPosition = _camera.EyePosition + new Vector3(-320.0f, -180.0f, 260.0f);
        _player.Initialize(_camera.WorldCenter);
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

            Update(deltaSeconds);
            _latency.Mark(LatencyMarker.SimulationEnd, frameId);

            await _renderer.RenderFrameAsync(
                _camera,
                _worldStreamer.VisibleWorld,
                _scene,
                ShouldPresentWithVSync(),
                _framePacingStatus,
                frameId,
                cancellationToken);
        }
    }

    private void Update(float deltaSeconds)
    {
        _player.ApplyPendingAssets();
        PollGamepad();

        if (_window.Input.ConsumePressed(VirtualKey.Tab) ||
            _window.Input.ConsumeXButtonCyclePressed())
        {
            _player.CycleModel();
        }

        if (_window.Input.ConsumePressed(VirtualKey.F4))
            _renderer.ToggleHdr();

        if (_window.Input.ConsumePressed(VirtualKey.F5))
        {
            _mode = NextFramePacingMode(_mode);
            UpdateWindowTitle();
        }

        if (_window.Input.ConsumePressed(VirtualKey.F6))
        {
            _latency.CycleMode();
            UpdateWindowTitle();
        }

        if (_window.Input.ConsumePressed(VirtualKey.F7))
        {
            _worldLighting.CycleMode();
            UpdateWindowTitle();
        }

        if (_worldLighting.Update(deltaSeconds, _scene.Lighting))
            UpdateWindowTitle();

        _clickToMove.Update(
            _window.Input,
            _camera,
            _window.ClientWidth,
            _window.ClientHeight,
            deltaSeconds);

        _camera.UpdateFromKeyboard(_window.Input, deltaSeconds);
        _player.UpdatePose(_camera.WorldCenter, _camera.CameraSpeedUnitVector, deltaSeconds);
        _worldStreamer.Update(_camera.WorldCenter);
    }

    private void PollGamepad()
    {
        var gamepads = Gamepad.Gamepads;
        var gamepad = gamepads.Count == 0 ? null : gamepads[0];
        if (gamepad is null)
        {
            _window.Input.LeftJoystickX = 0.0;
            _window.Input.LeftJoystickY = 0.0;
            _window.Input.RightJoystickY = 0.0;
            _window.Input.GamepadMoveFaster = false;
            _previousGamepadButtons = GamepadButtons.None;
            return;
        }

        var reading = gamepad.GetCurrentReading();
        var buttons = reading.Buttons;
        _window.Input.LeftJoystickX = reading.LeftThumbstickX;
        _window.Input.LeftJoystickY = reading.LeftThumbstickY;
        _window.Input.RightJoystickY = reading.RightThumbstickY;
        _window.Input.GamepadMoveFaster = (buttons & GamepadButtons.A) != 0;

        if ((buttons & GamepadButtons.B) != 0 &&
            (_previousGamepadButtons & GamepadButtons.B) == 0)
        {
            _player.CycleModel();
        }

        _previousGamepadButtons = buttons;
    }

    private bool ShouldPresentWithVSync() =>
        _mode == FramePacingMode.VSync ||
        (_mode == FramePacingMode.VariableRefreshRate && !_renderer.VariableRefreshRateSupported);

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
        _window.SetTitle(
            $"SacredEngineRemake - Pacing: {_framePacingStatus} - Lighting: {_worldLighting.DisplayName} - Low Latency: {lowLatencyMode} ({_latency.ActiveBackendName})");
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
