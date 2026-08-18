using System.IO.Compression;
using Sacred.Core.World;
using Sacred.Core.World.Sector;

namespace Sacred.World;

/// <summary>Extracts a complete sector payload when that sector streams into view.</summary>
public sealed class WldxLoader(FileStream wldxStream) : IDisposable
{
    private const int SectorW = Sector.TileCount;
    private const int SectorH = Sector.TileCount;

    private readonly object _streamLock = new();

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
        const int postTileHeaderSize = 0x24;
        const int descriptorSize = 0x24;
        const uint indoorTileKind = 6;

        var groups = new List<WldxIndoorGroupPayload>();
        var descriptorOffset = checked(tilesEnd + postTileHeaderSize);
        while (descriptorOffset <= data.Length - descriptorSize)
        {
            var descriptor = data.Slice(descriptorOffset, descriptorSize);
            var worldX = BitConverter.ToInt32(descriptor[..4]);
            var worldY = BitConverter.ToInt32(descriptor.Slice(4, 4));
            var width = BitConverter.ToUInt16(descriptor.Slice(8, 2));
            var height = BitConverter.ToUInt16(descriptor.Slice(10, 2));
            var kind = BitConverter.ToUInt32(descriptor.Slice(12, 4));
            var tilesOffset = BitConverter.ToUInt32(descriptor.Slice(16, 4));
            var tilesSize = BitConverter.ToUInt32(descriptor.Slice(20, 4));

            var expectedSize = (ulong)width * height * WldxTileRecord.Size;
            if (worldX < 0 || worldY < 0 || width == 0 || height == 0 ||
                kind != indoorTileKind || tilesSize != expectedSize ||
                tilesOffset > data.Length || tilesSize > data.Length - tilesOffset)
            {
                break;
            }

            groups.Add(new WldxIndoorGroupPayload(
                worldX,
                worldY,
                width,
                height,
                kind,
                data.Slice(checked((int)tilesOffset), checked((int)tilesSize)).ToArray()));
            descriptorOffset += descriptorSize;
        }

        return groups.ToArray();
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
    uint Kind,
    byte[] Tiles);
