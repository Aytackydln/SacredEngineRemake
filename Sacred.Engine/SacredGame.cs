using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
using Sacred.Granny;

namespace Sacred.Engine;

public sealed class SacredGame : IDisposable
{
    private const uint FirstPlayerModelSlotId = 1;
    private const float PlayerModelUprightPitch = 90.0f;

    private static FramePacingMode Mode = FramePacingMode.VariableRefreshRate;

    private readonly Win32Window _window;
    private readonly LowLatencySystem _latency;
    private readonly Dx12Renderer _renderer;
    private readonly AssetManager _assets;
    private readonly WorldStreamer _worldStreamer;
    private readonly SacredCamera _camera;
    private readonly ClickToMoveController _clickToMove = new();
    private readonly SceneState _scene = new();
    private readonly Mesh _playerProxyMesh = MeshFactory.CreateHumanoidProxyMesh();

    private Vector3 _playerPosition;
    private Vector3 _playerRotation;
    private float _playerMovementRotationZ = MathF.PI * 0.25f;
    private readonly uint _displayRefreshRateHz;

    private uint _activePlayerModelEntryId = FirstPlayerModelSlotId;
    private GamepadButtons _previousGamepadButtons;
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
        UpdateWindowTitle();

        BootstrapScene();
    }

    private void BootstrapScene()
    {
        _worldStreamer.CenterOnSector(_worldStreamer.StartSector.X, _worldStreamer.StartSector.Y);
        _camera.CenterOnTile(
            _worldStreamer.StartSector.X * WorldStreamer.SectorTileCount + WorldStreamer.SectorTileCount * 0.5f,
            _worldStreamer.StartSector.Y * WorldStreamer.SectorTileCount + WorldStreamer.SectorTileCount * 0.5f,
            0.75f);
        _scene.Lighting.LightPosition = _camera.EyePosition + new Vector3(-320.0f, -180.0f, 260.0f);

        _playerPosition = new Vector3(_camera.WorldCenter.X, _camera.WorldCenter.Y, 0.0f);
        _playerRotation = BuildPlayerRotation();

        _scene.Models.Add(new SceneModel(
            Name: "Loading player model",
            Mesh: _playerProxyMesh,
            Position: _playerPosition,
            Rotation: _playerRotation));
        _ = SetPlayerModelAsync(FirstPlayerModelSlotId);
    }

    public async Task Run(CancellationToken cancellationToken = default)
    {
        await Win32AsyncPump.RunAsync(() => RunCoreAsync(cancellationToken), _window.ProcessMessages);
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        var clock = new FrameClock(_displayRefreshRateHz);
        var previousFrameWorkTime = TimeSpan.Zero;
        var frameId = 0UL;

        while (!cancellationToken.IsCancellationRequested)
        {
            await clock.WaitForFrameStartAsync(Mode, previousFrameWorkTime, cancellationToken);
            _latency.SetMode(
                _latency.Mode,
                Mode == FramePacingMode.VSync ? 0 : clock.TargetFrameRate);

            frameId++;
            _latency.BeginFrame(frameId);
            _latency.SleepBeforeInput(frameId);

            if (!_window.ProcessMessages())
                break;

            var iterationStart = Stopwatch.GetTimestamp();
            var dt = clock.Tick();
            _latency.Mark(LatencyMarker.SimulationStart, frameId);
            if (_window.Input.HasPendingLeftClick)
                _latency.Mark(LatencyMarker.LeftMouseButtonClick, frameId);

            Update(dt);
            _latency.Mark(LatencyMarker.SimulationEnd, frameId);

            await _renderer.RenderFrameAsync(
                _camera,
                _worldStreamer.VisibleWorld,
                _scene,
                ShouldPresentWithVSync(),
                FormatFramePacingMode(),
                frameId,
                cancellationToken);

            previousFrameWorkTime = Stopwatch.GetElapsedTime(iterationStart);
        }
    }

    private void Update(float deltaSeconds)
    {
        var gamepad = Gamepad.Gamepads.FirstOrDefault();
        if (gamepad != null)
        {
            var gamepadReading = gamepad.GetCurrentReading();
            _window.Input.LeftJoystickX = gamepadReading.LeftThumbstickX;
            _window.Input.LeftJoystickY = gamepadReading.LeftThumbstickY;
            _window.Input.RightJoystickY = gamepadReading.RightThumbstickY;
            _window.Input.GamepadMoveFaster = gamepadReading.Buttons.HasFlag(GamepadButtons.A);

            if (gamepadReading.Buttons.HasFlag(GamepadButtons.B) &&
                !_previousGamepadButtons.HasFlag(GamepadButtons.B))
            {
                CyclePlayerModel();
            }

            _previousGamepadButtons = gamepadReading.Buttons;
        }
        else
        {
            _window.Input.LeftJoystickX = 0.0;
            _window.Input.LeftJoystickY = 0.0;
            _window.Input.RightJoystickY = 0.0;
            _window.Input.GamepadMoveFaster = false;
            _previousGamepadButtons = GamepadButtons.None;
        }

        if (_window.Input.ConsumePressed(VirtualKey.Tab) ||
            _window.Input.ConsumeXButtonCyclePressed())
        {
            CyclePlayerModel();
        }

        if (_window.Input.ConsumePressed(VirtualKey.F4))
            _renderer.ToggleHdr();

        if (_window.Input.ConsumePressed(VirtualKey.F5))
        {
            Mode = NextFramePacingMode(Mode);
            UpdateWindowTitle();
        }

        if (_window.Input.ConsumePressed(VirtualKey.F6))
        {
            _latency.CycleMode();
            UpdateWindowTitle();
        }

        _clickToMove.Update(
            _window.Input,
            _camera,
            _window.ClientWidth,
            _window.ClientHeight,
            deltaSeconds);

        _camera.UpdateFromKeyboard(_window.Input, deltaSeconds);
        _playerPosition = new Vector3(_camera.WorldCenter.X, _camera.WorldCenter.Y, 0.0f);

        // Pitch, Yaw, Roll from camera's speed vector:
        if (_camera.CameraSpeedUnitVector != Vector2.Zero)
        {
            var angleRadians = MathF.Atan2(_camera.CameraSpeedUnitVector.Y, _camera.CameraSpeedUnitVector.X);
            _playerMovementRotationZ = angleRadians + MathF.PI / 4; // 45 degree offset to match the isometric camera angle
        }

        _playerRotation = BuildPlayerRotation();
        _scene.Models[0] = _scene.Models[0] with { Position = _playerPosition, Rotation = _playerRotation };

        _worldStreamer.Update(_camera.WorldCenter);
    }

    private void CyclePlayerModel()
    {
        var next = _activePlayerModelEntryId >= (uint)_assets.PlayerCharacterCount
            ? FirstPlayerModelSlotId
            : _activePlayerModelEntryId + 1;

        _ = SetPlayerModelAsync(next);
    }

    private async Task SetPlayerModelAsync(uint entryId)
    {
        var player = await _assets.LoadPlayerCharacterAsync(entryId);
        _activePlayerModelEntryId = entryId;
        _playerRotation = BuildPlayerRotation();
        
        var sceneModel = new SceneModel(
            Name: $"{player.DisplayName}: item {player.ItemId}, {player.ModelName}",
            Mesh: player.Model.Mesh ?? _playerProxyMesh,
            Position: _playerPosition,
            Rotation: _playerRotation,
            TextureAliases: player.TextureAliases
        );

        if (_scene.Models.Count == 0)
            _scene.Models.Add(sceneModel);
        else
            _scene.Models[0] = sceneModel;
    }

    private Vector3 BuildPlayerRotation() =>
        new(0.0f, PlayerModelUprightPitch, _playerMovementRotationZ);

    private bool ShouldPresentWithVSync() =>
        Mode == FramePacingMode.VSync ||
        (Mode == FramePacingMode.VariableRefreshRate && !_renderer.VariableRefreshRateSupported);

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

        _window.SetTitle(
            $"SacredEngineRemake - Pacing: {FormatFramePacingMode()} - Low Latency: {lowLatencyMode} ({_latency.ActiveBackendName})");
    }

    private string FormatFramePacingMode() => Mode switch
    {
        FramePacingMode.VariableRefreshRate => _renderer.VariableRefreshRateSupported
            ? $"VRR, {_displayRefreshRateHz} FPS cap"
            : "VRR unavailable, VSync fallback",
        FramePacingMode.VSync => "VSync",
        FramePacingMode.MonitorRefreshLimiter => $"{_displayRefreshRateHz} FPS limiter",
        _ => Mode.ToString()
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Dispose();
        _latency.Dispose();
        _worldStreamer.Dispose();
        _assets.Dispose();
        _window.Dispose();
    }
}
