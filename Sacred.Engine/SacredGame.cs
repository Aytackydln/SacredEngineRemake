using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Windows.Gaming.Input;
using Sacred.Core;
using Sacred.Core.Assets;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics;
using Sacred.Engine.Platform;
using Sacred.Engine.Scene;
using Sacred.Engine.World;

namespace Sacred.Engine;

public sealed class SacredGame : IDisposable
{
    private const uint FirstPlayerModelSlotId = 1;

    private readonly Win32Window _window;
    private readonly Dx12Renderer _renderer;
    private readonly AssetManager _assets;
    private readonly WorldStreamer _worldStreamer;
    private readonly SacredCamera _camera;
    private readonly ClickToMoveController _clickToMove = new();
    private readonly SceneState _scene = new();
    private readonly Mesh _playerProxyMesh = MeshFactory.CreateHumanoidProxyMesh();

    private Vector3 _playerPosition;
    private Vector3 _playerRotation;

    private uint _activePlayerModelEntryId = FirstPlayerModelSlotId;
    private bool _disposed;

    public SacredGame(SacredGameDirectories gameDirectories)
    {
        _window = new Win32Window("Sacred Remake DX12 Prototype", 1600, 900);
        _assets = new AssetManager(gameDirectories);
        _camera = SacredCamera.CreateDefault(1600, 900);
        _worldStreamer = new WorldStreamer(SacredWorldArchive.Load(gameDirectories));
        _renderer = new Dx12Renderer(_window, _assets);

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
        _playerRotation = new Vector3(0.0f, 90.0f, 45.0f);

        SetPlayerModel(FirstPlayerModelSlotId);
    }

    public void Run()
    {
        var clock = new FrameClock();
        while (_window.ProcessMessages())
        {
            var dt = clock.Tick();
            Update(dt);
            _renderer.RenderFrame(_camera, _worldStreamer.VisibleWorld, _scene);
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
        }

        if (_window.Input.ConsumePressed(VirtualKey.Tab))
            CyclePlayerModel();

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
            _playerRotation.Z = angleRadians + MathF.PI / 4; // 45 degree offset to match the isometric camera angle
        }

        _scene.Models[0] = _scene.Models[0] with { Position = _playerPosition, Rotation = _playerRotation };

        _worldStreamer.Update(_camera.WorldCenter);
    }

    private void CyclePlayerModel()
    {
        var next = _activePlayerModelEntryId >= (uint)_assets.PlayerCharacterCount
            ? FirstPlayerModelSlotId
            : _activePlayerModelEntryId + 1;

        Task.Run(() => SetPlayerModel(next));
    }

    private void SetPlayerModel(uint entryId)
    {
        var player = _assets.LoadPlayerCharacter(entryId);
        _activePlayerModelEntryId = entryId;
        
        var sceneModel = new SceneModel(
            Name: $"{player.DisplayName}: {player.ModelName}",
            Mesh: player.Model.Mesh ?? _playerProxyMesh,
            Position: _playerPosition,
            Rotation: _playerRotation,
            SourceModel: player.Model
        );

        if (_scene.Models.Count == 0)
            _scene.Models.Add(sceneModel);
        else
            _scene.Models[0] = sceneModel;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _renderer.Dispose();
        _worldStreamer.Dispose();
        _assets.Dispose();
        _window.Dispose();
    }
}
