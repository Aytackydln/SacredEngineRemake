using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.World;

namespace Sacred.Engine.World;

public sealed class WldxLoader(FileStream wldxStream) : IDisposable
{
    private const int SectorW = Sector.TileCount;
    private const int SectorH = Sector.TileCount;

    private readonly SemaphoreSlim _wldxLock = new(1, 1);

    public async Task<byte[]> ReadWldx(KeyxSectorRecord entry, uint sectorId)
    {
        var compressed = await ReadWldxCompressed(entry, sectorId);

        byte[] decompressed;
        using (var compressedStream = new MemoryStream(compressed, writable: false))
        await using (var zlib = new ZLibStream(compressedStream, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            await zlib.CopyToAsync(output);
            decompressed = output.ToArray();
        }

        if (entry.TilesRelativeOffset < 0 ||
            entry.TilesSize < SectorW * SectorH * WldxTileRecord.Size ||
            entry.TilesRelativeOffset + entry.TilesSize > decompressed.Length)
            throw new InvalidDataException($"Sector {sectorId} has an invalid tile block.");

        return decompressed;
    }

    private async Task<byte[]> ReadWldxCompressed(KeyxSectorRecord entry, uint sectorId)
    {
        await _wldxLock.WaitAsync();
        wldxStream.Position = entry.CompressedOffset;

        if (entry.CompressedSize > int.MaxValue)
            throw new InvalidDataException($"Sector {sectorId} compressed block is too large.");

        var compressed = new byte[(int)entry.CompressedSize];
        await wldxStream.ReadExactlyAsync(compressed);
        _wldxLock.Release();
        return compressed;
    }

    public void Dispose()
    {
        _wldxLock.Dispose();
    }
}