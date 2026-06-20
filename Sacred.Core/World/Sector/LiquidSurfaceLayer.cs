namespace Sacred.Core.World.Sector;

public sealed class LiquidSurfaceLayer
{
    private readonly List<LiquidSurface> _surfaces = [];

    public int Count => _surfaces.Count;
    public IReadOnlyList<LiquidSurface> Surfaces => _surfaces;

    public void Add(LiquidSurface surface) => _surfaces.Add(surface);
}