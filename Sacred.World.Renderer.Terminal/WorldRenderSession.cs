using System.Numerics;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Mixed;
using Sacred.Assets.Paks.Models;
using Sacred.Assets.Paks.Texture;
using Sacred.Assets.Paks.Tiles;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Sector;
using Sacred.Engine.Rendering;
using Sacred.World.Rendering;

namespace Sacred.World.Renderer.Terminal;

/// <summary>Owns reusable renderers and bounds their retained data between offline batches.</summary>
internal sealed class WorldRenderSession : IDisposable
{
    private readonly string _pakDirectory;
    private bool _backendInitialized;
#if WINDOWS
    private GpuWorldImageRenderer? _gpu;
#endif
    private readonly SacredWorldArchive _world;
    private readonly TexturePakArchive _textures;
    private CachedTextureSource? _cachedTextures;
    private TilesPakArchive _tiles = null!;
    private MixedPakArchive _mixed = null!;
    private IReadOnlyDictionary<ushort, ItemsPakEntry> _items = null!;
    private ModelsPakArchive? _models;
    private DayWorldRasterizer _terrain = null!;
    private WorldModelRasterizer _modelRenderer = null!;
    private WorldParticleRasterizer _particles = null!;
    private WorldPortalRasterizer _portals = null!;
    private readonly WorldSpriteOcclusionCache _spriteOcclusion = new();

    public WorldRenderSession(string pakDirectory, SacredWorldArchive world, TexturePakArchive textures)
    {
        _pakDirectory = pakDirectory;
        _world = world;
        _textures = textures;
    }

    public async Task<(DayWorldRenderResult Terrain, RgbaImage Complete)> RenderAsync(Vector2 center,
        int width, int height, float zoom, RendererOptions options, IndoorTileGroup? indoorGroup = null,
        bool drawCenterMarker = true)
    {
        InitializeBackend(width, height, options);
        if (_models is null) ResetRenderers();
        _terrain.DrawCenterMarker = drawCenterMarker;
        _spriteOcclusion.Reset();
        // Sector mode only needs the final frame. Coordinate mode also exports
        // the original terrain image and must retain its separate pixel buffer.
        _modelRenderer.RenderInPlace = options.AllSectors || options.SectorX.HasValue;
        _particles.RenderInPlace = true;
        var terrain = await _terrain.RenderAsync(center, width, height, zoom,
            activeIndoorGroup: indoorGroup);
#if WINDOWS
        if (_gpu is not null)
        {
            var gpuImage = await _gpu.RenderAsync(center, width, height, zoom,
                options.ParticleSeconds, options.OpenDoors, indoorGroup);
            if (drawCenterMarker)
            {
                for (var offset = -6; offset <= 6; offset++)
                {
                    Mark(width / 2 + offset, height / 2);
                    Mark(width / 2, height / 2 + offset);
                }
                void Mark(int x, int y)
                {
                    if ((uint)x >= width || (uint)y >= height) return;
                    var index = (y * width + x) * 4;
                    gpuImage.Pixels[index] = 255;
                    gpuImage.Pixels[index + 1] = 224;
                    gpuImage.Pixels[index + 2] = 128;
                    gpuImage.Pixels[index + 3] = 255;
                }
            }
            return (terrain, gpuImage);
        }
#endif
        var models = await _modelRenderer.RenderAsync(terrain.Image, center, zoom, options.OpenDoors, indoorGroup);
        var complete = await _particles.RenderAsync(models, center, zoom, options.ParticleSeconds, indoorGroup);
        complete = await _portals.RenderAsync(complete, center, zoom, options.ParticleSeconds, indoorGroup);
        _spriteOcclusion.Reset();
        return (terrain, complete);
    }

    public Task<RgbaImage> RenderSectorAsync(Vector2 center, int width, int height, float zoom,
        RendererOptions options)
        => RenderSectorAsync(center, width, height, zoom, options, width, height);

    public async Task<RgbaImage> RenderSectorAsync(Vector2 center, int width, int height, float zoom,
        RendererOptions options, int outputWidth, int outputHeight)
    {
        InitializeBackend(width, height, options);
#if WINDOWS
        if (_gpu is not null)
            return await _gpu.RenderAsync(center, width, height, zoom, outputWidth, outputHeight,
                options.ParticleSeconds, options.OpenDoors);
#endif
        var image = (await RenderAsync(center, width, height, zoom, options, drawCenterMarker: false)).Complete;
        return RgbaImageDownsampler.Resize(image, outputWidth, outputHeight);
    }

    private void InitializeBackend(int width, int height, RendererOptions options)
    {
        if (_backendInitialized) return;
        _cachedTextures = new CachedTextureSource(_textures, (long)options.TextureCacheMegabytes * 1024 * 1024);
#if WINDOWS
        if (options.Backend == RendererBackend.Gpu)
        {
            _gpu = new GpuWorldImageRenderer(
                Directory.GetParent(_pakDirectory)!.FullName, _world, width, height, _cachedTextures);
        }
        RendererLog.Info(_gpu is null ? "Renderer: software." : "Renderer: DX12 GPU (offscreen).");
#else
        if (options.Backend == RendererBackend.Gpu)
            throw new PlatformNotSupportedException("The GPU renderer requires Windows and Direct3D 12.");
        RendererLog.Info("Renderer: software.");
#endif
        _backendInitialized = true;
    }

    public void ReleaseBatch()
    {
        _world.ClearLoadedSectorCache();
#if WINDOWS
        _gpu?.ReleaseBatch();
#endif
        if (_models is not null) ResetRenderers();
    }

    private void ResetRenderers()
    {
        if (_models is null)
        {
            _tiles = TilesPakArchive.Load(Path.Combine(_pakDirectory, "tiles.pak"));
            _items = ItemsPakArchive.Load(Path.Combine(_pakDirectory, "Items.pak")).ToDictionary(i => i.ItemIndex);
            _mixed = MixedPakArchive.Load(Path.Combine(_pakDirectory, "mixed.pak"));
            _models = ModelsPakArchive.Load(Path.Combine(_pakDirectory, "models.pak"), Path.Combine(_pakDirectory, "Models.tmp"));
        }
        ITextureSource textures = _cachedTextures ?? (ITextureSource)_textures;
        var sprites = new WorldStaticSpriteProvider(textures, _mixed, _items);
        _terrain = new DayWorldRasterizer(_world, textures, _tiles, sprites);
        _spriteOcclusion.Reset();
        _modelRenderer = new WorldModelRasterizer(_world, _items, _models, textures)
            { StaticSprites = sprites, SpriteOcclusionCache = _spriteOcclusion };
        _particles = new WorldParticleRasterizer(_world, textures, sprites) { SpriteOcclusionCache = _spriteOcclusion };
        _portals = new WorldPortalRasterizer(_world, textures, sprites) { SpriteOcclusionCache = _spriteOcclusion };
    }

    public void Dispose()
    {
#if WINDOWS
        _gpu?.Dispose();
#endif
        _models?.Dispose();
        _cachedTextures?.Dispose();
    }

    public (long Hits, long Misses) TextureCacheStatistics
    {
        get
        {
            return _cachedTextures?.Statistics ?? (0, 0);
        }
    }
}
