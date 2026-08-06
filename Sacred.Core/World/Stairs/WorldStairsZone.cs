namespace Sacred.Core.World.Stairs;

public sealed class WorldStairsZone
{
    internal WorldStairsZone(WorldStairsCoordinate anchor, WorldStairsCell[] cells)
    {
        Anchor = anchor;
        Cells = cells;
    }

    public WorldStairsCoordinate Anchor { get; }
    public IReadOnlyList<WorldStairsCell> Cells { get; }

    public bool ContainsWithMargin(float worldX, float worldY, float margin)
    {
        foreach (var cell in Cells)
        {
            var position = cell.Position;
            if (worldX >= position.X - margin && worldX < position.X + 1.0f + margin &&
                worldY >= position.Y - margin && worldY < position.Y + 1.0f + margin)
            {
                return true;
            }
        }

        return false;
    }
}
