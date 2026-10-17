using System.Collections.Frozen;
using WorldSector = Sacred.Core.World.Sector.Sector;

namespace Sacred.Core.World;

/// <summary>Native 128 x 128 sector lookup built from original KEYX bounds and the ID-indexed sector table.</summary>
public sealed class SacredSectorGrid
{
    private readonly ushort[] _cells = new ushort[SacredSectorCoordinates.GridWidth * SacredSectorCoordinates.GridWidth];
    private readonly FrozenSet<uint> _present;
    public uint TableSize { get; }
    internal SacredSectorGrid(IReadOnlyDictionary<uint, KeyxSectorRecord> records, uint tableSize)
    {
        TableSize = tableSize;
        _present = records.Keys.Where(id => id < tableSize).ToFrozenSet();
        // Native grid building walks sector pointer slots in ascending order; a later slot overwrites the same cell.
        foreach (var entry in records.OrderBy(pair => pair.Key))
        {
            if (entry.Key >= tableSize) throw new InvalidDataException("KEYX sector ID lies outside its declared native table.");
            var grid = SacredSectorCoordinates.ToGrid(entry.Value);
            if ((uint)grid.X >= SacredSectorCoordinates.GridWidth || (uint)grid.Y >= SacredSectorCoordinates.GridWidth)
                throw new InvalidDataException("KEYX sector origin lies outside the native grid.");
            _cells[grid.Y * SacredSectorCoordinates.GridWidth + grid.X] = unchecked((ushort)entry.Key);
        }
    }
    public uint GetSector(int tileX, int tileY)
    {
        if (tileX < 0 || tileY < 0) return 0;
        // Gold 0x63C9D4..0x63C9FE tests and indexes the low 16 bits of each quotient.
        var x = unchecked((ushort)(tileX / WorldSector.TileCount));
        var y = unchecked((ushort)(tileY / WorldSector.TileCount));
        if (x >= SacredSectorCoordinates.GridWidth || y >= SacredSectorCoordinates.GridWidth)
            return 0;
        return _cells[y * SacredSectorCoordinates.GridWidth + x];
    }
    public bool HasSector(uint sector) => sector < TableSize && _present.Contains(sector);
}
