using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Sacred.Assets.Paks.Texture;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Assets;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny.Animation;
using Sacred.World;
using Sacred.World.Geometry;
using Sacred.World.Objects;
using Sacred.World.Particles;
using Sacred.World.Rendering;
using WorldImage = Sacred.World.Rendering.RgbaImage;

namespace Sacred.Engine.Rendering;

/// <summary>Renders world textures with the game's DX12 passes, without a window or input.</summary>
public sealed class GpuWorldImageRenderer : IDisposable
{
    private readonly SacredWorldArchive _world;
    private AssetManager _assets;
    private readonly TexturePakArchive _textures;
    private readonly ITextureSource? _textureSource;
    private readonly string _gameDirectory;
    private readonly LowLatencySystem _latency;
    private readonly Dx12Renderer _renderer;
    private readonly SceneState _scene = new();
    private readonly Dictionary<uint, WorldModelAsset?> _models = [];

    public GpuWorldImageRenderer(string gameDirectory, SacredWorldArchive world, int width, int height,
        ITextureSource? textureSource)
    {
        _world = world;
        _textureSource = textureSource;
        _gameDirectory = gameDirectory;
        _latency = LowLatencySystem.CreateDefault();
        _latency.SetMode(LowLatencyMode.Off, 0);
        _textures = TexturePakArchive.LoadFromDirectory(Path.Combine(gameDirectory, "pak"));
        Dx12Renderer? renderer = null;
        AssetManager? assets = null;
        try
        {
            _assets = assets = OfflineWorldAssets.Load(gameDirectory, _textures, textureSource);
            renderer = new Dx12Renderer(gameDirectory, width, height, _latency);
            renderer.InitializeWorld(_assets, world);
            _renderer = renderer;
        }
        catch
        {
            renderer?.Dispose();
            assets?.Dispose();
            _textures.Dispose();
            _latency.Dispose();
            throw;
        }
        _scene.Debug.OverlaysVisible = false;
    }

    public Task<WorldImage> RenderAsync(Vector2 center, int width, int height, float zoom,
        float particleSeconds = 2, bool openDoors = false, IndoorTileGroup? indoorGroup = null)
        => RenderAsync(center, width, height, zoom, width, height, particleSeconds, openDoors, indoorGroup);

    /// <summary>Reduces the completed frame on the GPU and reads back only the output pixels.</summary>
    public async Task<WorldImage> RenderAsync(Vector2 center, int width, int height, float zoom,
        int outputWidth, int outputHeight, float particleSeconds = 2, bool openDoors = false,
        IndoorTileGroup? indoorGroup = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputWidth);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(outputHeight);
        if (!float.IsFinite(particleSeconds) || particleSeconds < 0)
            throw new ArgumentOutOfRangeException(nameof(particleSeconds));
        if (outputWidth > width || outputHeight > height)
            throw new ArgumentException("Area downsampling requires dimensions no larger than the render target.");
        var origin = new SectorCoord((int)MathF.Floor(center.X / Sector.TileCount),
            (int)MathF.Floor(center.Y / Sector.TileCount));
        var loads = new List<Task<Sector?>>(9);
        for (var y = -1; y <= 1; y++)
        for (var x = -1; x <= 1; x++)
            loads.Add(_world.TryLoadSector(new SectorCoord(origin.X + x, origin.Y + y)));
        var sectors = (await Task.WhenAll(loads).ConfigureAwait(false)).OfType<Sector>().ToArray();
        if (sectors.Length == 0)
            throw new InvalidOperationException("No sectors exist at this export view.");
        var world = new VisibleWorld(origin, sectors, 0);
        await PrepareSceneModelsAsync(center, openDoors, indoorGroup, sectors);
        var particles = new WorldParticleSystem(_world.ParticleScript);
        particles.Update(0, world, indoorGroup);
        if (particles.ActiveEmitterCount > 0)
        {
            var steps = (int)Math.Ceiling(particleSeconds * 60.0);
            for (var step = 0; step < steps; step++)
                particles.Update(Math.Min(1f / 60, particleSeconds - step / 60f), world, indoorGroup);
        }
        var camera = SacredCamera.CreateDefault(width, height);
        camera.SetExportView(center.X, center.Y, zoom);
        _renderer.SetOffscreenAnimationTime(particleSeconds);
        return await _renderer.RenderImageAsync(width, height, outputWidth, outputHeight,
            camera, world, _scene, particles).ConfigureAwait(false);
    }

    private async Task PrepareSceneModelsAsync(Vector2 center, bool openDoors, IndoorTileGroup? indoorGroup,
        Sector[] sectors)
    {
        _scene.ClearModels();
        _scene.Indoor.ActiveGroup = indoorGroup;
        new WorldLightingController(WorldLightingMode.Day).Update(0, _scene.Lighting,
            indoorGroup is null ? _world.GetZone(center.X, center.Y) : WorldZone.Indoors);
        var groups = sectors.SelectMany(s => s.IndoorTileGroups.Groups).DistinctBy(g => g.Id).ToArray();
        var placements = sectors.SelectMany(s => s.WorldObjects.Objects.Concat(s.StaticObjects.Objects))
            .OrderByDescending(p => p.PreciseWorldPosition.HasValue)
            .DistinctBy(p => (p.TypeId, p.TileWorldX, p.TileWorldY));
        foreach (var placement in placements)
        {
            var item = _assets.GetItem(placement.TypeId);
            if (item is not { } value || !value.ModelDesc.GraphicType.HasFlag(SacredItemGraphicType.Model)
                                      || value.ModelDesc.Category is not (SacredItemCategory.WorldObject or SacredItemCategory.Container
                                          or SacredItemCategory.Door or SacredItemCategory.Effect)
                                      || !WorldObjectSurfaceVisibility.IsModelVisible(placement, groups, indoorGroup, value.ModelDesc.Category))
                continue;
            if (!_models.TryGetValue(placement.TypeId, out var asset))
            {
                try
                {
                    asset = await _assets.LoadWorldModelAsync(placement.TypeId).ConfigureAwait(false);
                }
                catch (FileNotFoundException)
                {
                    asset = null;
                }
                _models[placement.TypeId] = asset;
            }
            if (asset?.Model.Mesh is not { } mesh)
                continue;
            GrnAnimatedMesh? animatedMesh = null;
            if (openDoors && value.ModelDesc.Category == SacredItemCategory.Door)
            {
                var motion = new DoorMotionPlayback(mesh, asset.Model.Skin, asset.OpenAnimation, asset.CloseAnimation);
                motion.SetInitialState(true);
                mesh = motion.Mesh;
                animatedMesh = motion.AnimatedMesh;
            }
            var model = new SceneModel(value.ModelName, mesh, new Vector3(WorldModelPose.TilePosition(placement), 0),
                    new Vector3(0, 0, WorldModelPose.RotationRadians(value.ModelDesc.Angle3D, placement.ScriptFacingDegrees)),
                    WorldModelPose.Scale, asset.TextureAliases,
                    sourceOriginOffset: asset.Model.Diagnostics?.SourceOriginOffset ?? Vector3.Zero,
                    blockRadius: value.ModelDesc.BlockRadius)
                { IsWorldObject = true };
            model.SetPose(model.Position, model.Rotation, new Vector2(placement.TileWorldX, placement.TileWorldY));
            model.SetModelProjection(WorldModelPose.CameraProjection);
            if (animatedMesh is not null) model.SetAnimatedMesh(animatedMesh);
            _scene.AddModel(model);
        }
    }

    public void ReleaseBatch()
    {
        _renderer.ClearOffscreenWorld();
        _assets.Dispose();
        _models.Clear();
        _scene.ClearModels();
        _assets = OfflineWorldAssets.Load(_gameDirectory, _textures, _textureSource);
        _renderer.InitializeWorld(_assets, _world);
    }

    public void Dispose()
    {
        _renderer.Dispose();
        _assets.Dispose();
        _textures.Dispose();
        _latency.Dispose();
    }
}
