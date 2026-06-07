namespace Sacred.Assets.Paks.Tiles;

public readonly record struct TileDefinition(string FileName, uint TileNumber)
{
    public static readonly TileDefinition Empty = new(string.Empty, 0);
}