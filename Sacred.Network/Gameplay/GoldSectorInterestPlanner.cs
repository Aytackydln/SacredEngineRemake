using Sacred.Core.World;

namespace Sacred.Network.Gameplay;

/// <summary>Native sector-neighborhood difference, retaining KEYX neighbor order and center in slot eight.</summary>
public static class GoldSectorInterestPlanner
{
    public static GoldSectorInterestRequest Create(SacredSectorIndex index, ushort currentSector, ushort previousSector, uint freeReferenceCount)
    {
        ArgumentNullException.ThrowIfNull(index);
        if (currentSector == 0 || !index.TryGetSector(currentSector, out _))
            throw new InvalidDataException("Current sector is absent from the authored index.");
        if (previousSector != 0 && !index.TryGetSector(previousSector, out _))
            throw new InvalidDataException("Previous sector is absent from the authored index.");
        var entered = Neighborhood(index, currentSector);
        var left = Neighborhood(index, previousSector);
        // Compare original neighborhoods, as native 0x6263B0 does; zero all shared occurrences in either list.
        var originalEntered = entered.ToArray();
        var originalLeft = left.ToArray();
        for (var i = 0; i < entered.Length; i++)
        for (var j = 0; j < left.Length; j++)
            if (originalEntered[i] == originalLeft[j]) { entered[i] = 0; left[j] = 0; }
        return GoldSectorInterestRequest.Create(currentSector, previousSector, entered, left, freeReferenceCount);
    }

    private static ushort[] Neighborhood(SacredSectorIndex index, ushort sector)
    {
        var result = new ushort[GoldSectorInterestLayout.SlotCount];
        if (sector == 0 || !index.TryGetSector(sector, out var record)) return result;
        for (var i = 0; i < 8; i++)
            if (record.Neighbors[i] != 0 && index.TryGetSector(record.Neighbors[i], out _)) result[i] = record.Neighbors[i];
        result[8] = sector;
        return result;
    }
}
