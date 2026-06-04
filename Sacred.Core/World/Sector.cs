namespace Sacred.Core.World;

public sealed class Sector
{
    public const int TileCount = 64;
    public const int TileSize = 64;
    public const int PixelSize = TileCount * TileSize;

    public SectorCoord Coord { get; }
    public TileLayer Ground { get; }
    public FloorOverlayLayer FloorOverlays { get; }
    public LiquidSurfaceLayer LiquidSurfaces { get; }
    public StaticObjectLayer StaticObjects { get; }

    public Sector(
        SectorCoord coord,
        TileLayer ground,
        FloorOverlayLayer floorOverlays,
        LiquidSurfaceLayer liquidSurfaces,
        StaticObjectLayer staticObjects)
    {
        Coord = coord;
        Ground = ground;
        FloorOverlays = floorOverlays;
        LiquidSurfaces = liquidSurfaces;
        StaticObjects = staticObjects;
    }

    public static Sector GenerateDebugSector(SectorCoord coord)
    {
        var ground = new TileLayer(TileCount, TileCount);
        for (var y = 0; y < ground.Height; y++)
        for (var x = 0; x < ground.Width; x++)
            ground[x, y] = (uint)((x + y + coord.X * 3 + coord.Y * 7) & 3);

        return new Sector(
            coord,
            ground,
            new FloorOverlayLayer(TileCount, TileCount),
            new LiquidSurfaceLayer(),
            new StaticObjectLayer());
    }
}

public readonly record struct SectorCoord(int X, int Y);

public sealed record VisibleWorld(SectorCoord CenterSector, IReadOnlyList<Sector> Sectors, int LoadingSectors)
{
    public static readonly VisibleWorld Empty = new(new SectorCoord(0, 0), [], 0);
}

public sealed class TileLayer
{
    private readonly uint[] _tiles;

    public int Width { get; }
    public int Height { get; }

    public TileLayer(int width, int height)
    {
        Width = width;
        Height = height;
        _tiles = new uint[width * height];
    }

    public uint this[int x, int y]
    {
        get => _tiles[y * Width + x];
        set => _tiles[y * Width + x] = value;
    }
}

public sealed class FloorOverlayLayer
{
    private readonly List<FloorOverlay>[] _overlays;

    public int Width { get; }
    public int Height { get; }
    public int Count { get; private set; }

    public FloorOverlayLayer(int width, int height)
    {
        Width = width;
        Height = height;
        _overlays = new List<FloorOverlay>[width * height];
    }

    public IReadOnlyList<FloorOverlay> this[int x, int y] => _overlays[y * Width + x] ?? [];

    public void Add(int x, int y, FloorOverlay overlay)
    {
        var index = y * Width + x;
        var overlays = _overlays[index];
        if (overlays is null)
        {
            overlays = new List<FloorOverlay>();
            _overlays[index] = overlays;
        }

        overlays.Add(overlay);
        Count++;
    }
}

public readonly record struct FloorOverlay(uint TileOrBlendRef, uint PrimaryTileId, uint SecondaryTileId, int ChainDepth);

public sealed class LiquidSurfaceLayer
{
    private readonly List<LiquidSurface> _surfaces = new();

    public int Count => _surfaces.Count;
    public IReadOnlyList<LiquidSurface> Surfaces => _surfaces;

    public void Add(LiquidSurface surface) => _surfaces.Add(surface);
}

public readonly record struct LiquidSurface(
    int LocalX,
    int LocalY,
    byte SurfaceType,
    byte StyleId,
    sbyte AlphaLeft,
    sbyte AlphaTop,
    sbyte AlphaRight,
    sbyte AlphaBottom);

public sealed class StaticObjectLayer
{
    private readonly List<StaticWorldObject> _objects = new();

    public int Count => _objects.Count;
    public IReadOnlyList<StaticWorldObject> Objects => _objects;

    public void Add(StaticWorldObject staticObject) => _objects.Add(staticObject);
}

public readonly record struct StaticWorldObject(
    uint StaticId,
    uint TypeId,
    uint Flags,
    ushort SectorId,
    int ProjectedX,
    int ProjectedY,
    uint NextStaticId,
    short SurfaceRenderLayer,
    int TileDepth,
    int TileWorldY,
    int TileWorldX,
    int ChainDepth,
    int InsertionOrder);
