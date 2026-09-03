using System.IO.Compression;
using Sacred.Core.World;
using Sacred.Core.World.Sector;

namespace Sacred.World;

/// <summary>Extracts a complete sector payload when that sector streams into view.</summary>
public sealed class WldxLoader(FileStream wldxStream) : IDisposable
{
    private const int SectorW = Sector.TileCount;
    private const int SectorH = Sector.TileCount;

    private readonly Lock _streamLock = new();

    public WldxSectorPayload LoadSector(uint sectorId, KeyxSectorRecord entry)
    {
        const int requiredTileBytes = checked(SectorW * SectorH * WldxTileRecord.Size);
        if (entry.TilesRelativeOffset < 0 || entry.TilesSize < requiredTileBytes)
            throw new InvalidDataException($"Sector {sectorId} has an invalid tile block.");
        if (entry.CompressedSize > int.MaxValue ||
            (ulong)entry.CompressedOffset + entry.CompressedSize > (ulong)wldxStream.Length)
        {
            throw new InvalidDataException($"Sector {sectorId} has an invalid compressed block.");
        }

        lock (_streamLock)
        {
            wldxStream.Position = entry.CompressedOffset;
            using var zlib = new ZLibStream(wldxStream, CompressionMode.Decompress, leaveOpen: true);
            using var decompressed = new MemoryStream(checked((int)Math.Max(entry.TilesSize, requiredTileBytes)));
            zlib.CopyTo(decompressed);
            var data = decompressed.GetBuffer().AsSpan(0, checked((int)decompressed.Length));
            var tilesEnd = checked(entry.TilesRelativeOffset + requiredTileBytes);
            if (tilesEnd > data.Length)
                throw new InvalidDataException($"Sector {sectorId} has a truncated tile block.");

            return new WldxSectorPayload(
                data.Slice(entry.TilesRelativeOffset, requiredTileBytes).ToArray(),
                ReadIndoorGroups(data, tilesEnd));
        }
    }

    private static WldxIndoorGroupPayload[] ReadIndoorGroups(
        ReadOnlySpan<byte> data,
        int tilesEnd)
    {
        const int descriptorSize = WldxTileGridDescriptorLayout.SerializedSize;

        var groups = new List<WldxIndoorGroupPayload>();
        if (tilesEnd > data.Length - descriptorSize)
            return [];

        var outdoorOrigin = WldxTileGridDescriptorLayout.FromBytes(data[tilesEnd..]);
        if (!outdoorOrigin.IsOutdoorOrigin)
            return [];

        var descriptorOffset = checked(tilesEnd + descriptorSize);
        while (descriptorOffset <= data.Length - descriptorSize)
        {
            var descriptor = WldxTileGridDescriptorLayout.FromBytes(data[descriptorOffset..]);
            if (descriptor.WorldX < 0 || descriptor.WorldY < 0 ||
                !descriptor.HasIndoorTilePayload(data.Length))
                break;

            groups.Add(new WldxIndoorGroupPayload(
                descriptor.WorldX,
                descriptor.WorldY,
                descriptor.Width,
                descriptor.Height,
                descriptor.Kind,
                data.Slice(checked((int)descriptor.TilesOffset), checked((int)descriptor.TilesSize)).ToArray()));
            descriptorOffset += descriptorSize;
        }

        return [.. groups];
    }

    public void Dispose() => wldxStream.Dispose();
}

public sealed record WldxSectorPayload(
    byte[] OutdoorTiles,
    IReadOnlyList<WldxIndoorGroupPayload> IndoorGroups);

public sealed record WldxIndoorGroupPayload(
    int WorldX,
    int WorldY,
    int Width,
    int Height,
    WldxTileGridKind Kind,
    byte[] Tiles);
