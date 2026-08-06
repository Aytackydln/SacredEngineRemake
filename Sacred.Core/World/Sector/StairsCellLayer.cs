using Sacred.Core.World.Stairs;

namespace Sacred.Core.World.Sector;

public sealed class StairsCellLayer
{
    private readonly List<WorldStairsCell> _cells = [];

    public IReadOnlyList<WorldStairsCell> Cells => _cells;
    public int Count => _cells.Count;

    public void Add(WorldStairsCell cell) => _cells.Add(cell);
}
