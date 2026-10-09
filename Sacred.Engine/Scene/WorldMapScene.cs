using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.GameBin.Scripts;
using Sacred.Engine.Platform;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene.WorldMap;
using Sacred.UI.Maps;
using Sacred.World.Map;

namespace Sacred.Engine.Scene;

internal sealed class WorldMapScene : IGameScene
{
    private readonly InputState _input;
    private readonly Win32Window _window;
    private readonly Func<Vector2> _getPlayerWorldPosition;
    private readonly Func<WorldMapAnnotations> _getAnnotations;
    private readonly WorldMapControls _controls;
    private readonly WorldMapCamera _camera = new();
    private readonly WorldMapInputController _inputController;
    private readonly WorldMapUiState _ui = new();
    private readonly WorldMapUiInputController _uiInput;
    private bool _closeRequested;
    private readonly PlaceholderScreenRasterizer _placeholderRasterizer;
    private readonly WorldMapAtlasLoader _loader;
    private readonly Dictionary<WorldMapKind, Task<WorldMapAtlas>> _loads = new();
    private readonly Task<IReadOnlyDictionary<SacredRegionNpcKind, TextureAsset>> _npcTextures;
    private readonly HashSet<WorldMapKind> _reportedFailures = new();
    private ScreenFrame _screen;
    private ScreenFrame? _mapFrame;
    private WorldMapAtlas? _atlas;
    private WorldMapAnnotations _annotations = WorldMapAnnotations.Empty;
    private WorldMapKind _selectedMap;
    private bool _showNpcs;
    private int _viewportWidth;
    private int _viewportHeight;
    private ulong _mapRevision;

    public WorldMapScene(Win32Window window, GamepadInputSource gamepad,
        Action<GameSceneId> requestSwitch, Action<Vector2> teleport,
        Func<string, CancellationToken, Task<TextureAsset>> loadTextureAsync,
        Func<Vector2> getPlayerWorldPosition, Func<WorldMapAnnotations> getAnnotations,
        WorldMapControls controls, string gameDirectory)
    {
        _window = window;
        _input = window.Input;
        _getPlayerWorldPosition = getPlayerWorldPosition;
        _getAnnotations = getAnnotations;
        _controls = controls;
        _inputController = new WorldMapInputController(_input, gamepad, _camera, teleport, requestSwitch);
        _uiInput = new(_ui, _input, () => new(window.ClientWidth, window.ClientHeight));
        _ui.CloseRequested += () =>
        {
            _closeRequested = true;
            EngineLog.WriteLine("Debug input: world map Close activated; returning to game.");
            requestSwitch(GameSceneId.InGame);
        };
        _placeholderRasterizer = new PlaceholderScreenRasterizer(gameDirectory);
        _screen = _placeholderRasterizer.Rasterize("WORLD MAP", "LOADING MAP...");
        _loader = new WorldMapAtlasLoader(loadTextureAsync);
        _npcTextures = WorldMapNpcTextures.LoadAsync(loadTextureAsync);
    }

    public GameSceneId Id => GameSceneId.WorldMap;

    public void OnActivated()
    {
        _input.ClearTransientEvents();
        _ui.Visible = true;
        _closeRequested = false;
        _uiInput.Reset();
        _inputController.Reset();
        _window.RequestFocus();
        _annotations = _getAnnotations();
        _controls.SelectedMap = WorldMapProjection.GetMap(_getPlayerWorldPosition());
        SelectMap(_controls.SelectedMap);
        EngineLog.WriteLine("World map scene activated.");
    }

    internal void FitMap()
    {
        if (_atlas is null) return;
        _camera.Fit(_atlas.Width, _atlas.Height, _window.ClientWidth, _window.ClientHeight);
        EngineLog.WriteLine($"World map {_selectedMap} fitted to viewport.");
    }

    public void OnDeactivated()
    {
        _ui.Visible = false;
        _uiInput.Reset();
        _inputController.Reset();
    }

    internal bool TrySetUiCheat(string value, out string message) => _uiInput.TryCheat(value, out message);

    public void Update(float deltaSeconds)
    {
        _uiInput.Update();
        if (_closeRequested) return;
        if (_selectedMap != _controls.SelectedMap) SelectMap(_controls.SelectedMap);
        ApplyLoadedAtlas();
        if (_atlas is null)
        {
            _inputController.Update(deltaSeconds, 2048, 2048, _window.ClientWidth, _window.ClientHeight);
            return;
        }
        if (_showNpcs != _controls.RegionNpcsVisible) RebuildFrame();
        if (_controls.FitRequested)
        {
            _controls.FitRequested = false;
            FitMap();
        }
        var width = _window.ClientWidth;
        var height = _window.ClientHeight;
        if (width != _viewportWidth || height != _viewportHeight)
        {
            _viewportWidth = width;
            _viewportHeight = height;
            _camera.Pan(Vector2.Zero, _atlas.Width, _atlas.Height, width, height);
        }
        _inputController.Update(deltaSeconds, _atlas.Width, _atlas.Height, width, height);
    }

    public ValueTask RenderAsync(SceneRenderContext context)
    {
        var frame = _mapFrame ?? _screen;
        var center = _mapFrame is not null ? _camera.Center : new Vector2(frame.Width, frame.Height) * 0.5f;
        var zoom = _mapFrame is not null ? _camera.Zoom
            : Math.Min(_window.ClientWidth / (float)frame.Width, _window.ClientHeight / (float)frame.Height);
        var overlay = new WorldMapOverlay(_inputController.TargetWorldPosition,
            _inputController.TargetScreenPosition,
            _mapFrame is not null && _inputController.IsControllerTargetVisible,
            _mapFrame is not null && (_inputController.IsMinimapVisible || _controls.MinimapVisible), "Silver", string.Empty);
        return context.Renderer.RenderWorldMapAsync(new(frame, center, zoom, overlay,
            _controls, _mapFrame is not null ? _annotations : null, _selectedMap, _ui),
            context.VerticalSyncEnabled, context.FrameId, context.CancellationToken);
    }

    private void SelectMap(WorldMapKind kind)
    {
        _selectedMap = kind;
        _inputController.MapAvailable = false;
        _atlas = null;
        _mapFrame = null;
        _inputController.Reset();
        _inputController.MapKind = kind;
        if (!_loads.ContainsKey(kind)) _loads.Add(kind, _loader.LoadAsync(kind));
        _screen = _placeholderRasterizer.Rasterize("WORLD MAP", $"LOADING {kind.ToString().ToUpperInvariant()} MAP...");
        EngineLog.WriteLine($"Debug input: world map {kind}.");
        ApplyLoadedAtlas();
    }

    private void ApplyLoadedAtlas()
    {
        if (_atlas is not null || !_loads.TryGetValue(_selectedMap, out var load) ||
            !load.IsCompleted || !_npcTextures.IsCompleted) return;
        if (!load.IsCompletedSuccessfully || !_npcTextures.IsCompletedSuccessfully)
        {
            if (_reportedFailures.Add(_selectedMap))
            {
                EngineLog.WriteLine($"World map {_selectedMap} failed to load: {load.Exception ?? _npcTextures.Exception}");
                _screen = _placeholderRasterizer.Rasterize("WORLD MAP", "MAP ASSETS COULD NOT BE LOADED");
            }
            return;
        }
        _atlas = load.Result;
        _inputController.MapAvailable = true;
        _viewportWidth = _window.ClientWidth;
        _viewportHeight = _window.ClientHeight;
        var player = WorldMapProjection.WorldToMap(_getPlayerWorldPosition(), _atlas.Width, _selectedMap);
        var center = WorldMapProjection.Contains(player, _atlas) ? player : new Vector2(_atlas.Width, _atlas.Height) * 0.5f;
        _camera.CenterOn(center, _atlas.Width, _atlas.Height, _viewportWidth, _viewportHeight);
        _inputController.PlaceControllerPointer(center, _atlas.Width, _atlas.Height);
        RebuildFrame();
        EngineLog.WriteLine($"World map {_selectedMap} ready, centered at {center.X:0.0},{center.Y:0.0}.");
    }

    private void RebuildFrame()
    {
        if (_atlas is null) return;
        _showNpcs = _controls.RegionNpcsVisible;
        _mapFrame = WorldMapFrameBuilder.Create(_atlas, _getPlayerWorldPosition(), ++_mapRevision,
            _annotations, _npcTextures.Result, _showNpcs);
        EngineLog.WriteLine($"World map region NPCs {(_showNpcs ? "visible" : "hidden")}.");
    }

    public void Dispose()
    {
        try { Task.WhenAll(_loads.Values).GetAwaiter().GetResult(); _npcTextures.GetAwaiter().GetResult(); }
        catch { /* Loading failures are reported while the scene is active. */ }
        _placeholderRasterizer.Dispose();
    }
}
