using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core.World;
using Sacred.Core.World.Sector;

namespace Sacred.Engine.World;

public sealed class WldxLoader : IDisposable
{
    private const int SectorW = Sector.TileCount;
    private const int SectorH = Sector.TileCount;

    private readonly SemaphoreSlim _wldxLock = new(1, 1);
    private readonly FileStream _wldxStream;

    public WldxLoader(FileStream wldxStream) =>
        _wldxStream = wldxStream ?? throw new ArgumentNullException(nameof(wldxStream));

    public async Task<byte[]> ReadSectorTiles(
        KeyxSectorRecord entry,
        uint sectorId,
        CancellationToken cancellationToken = default)
    {
        var requiredTileBytes = checked(SectorW * SectorH * WldxTileRecord.Size);
        if (entry.TilesRelativeOffset < 0 || entry.TilesSize < requiredTileBytes)
            throw new InvalidDataException($"Sector {sectorId} has an invalid tile block.");

        await _wldxLock.WaitAsync(cancellationToken);
        try
        {
            ValidateCompressedBlock(entry, sectorId);
            _wldxStream.Position = entry.CompressedOffset;

            await using var zlib = new ZLibStream(_wldxStream, CompressionMode.Decompress, leaveOpen: true);
            await SkipExactlyAsync(zlib, entry.TilesRelativeOffset, sectorId, cancellationToken);

            var tiles = GC.AllocateUninitializedArray<byte>(requiredTileBytes);
            try
            {
                await zlib.ReadExactlyAsync(tiles, cancellationToken);
            }
            catch (EndOfStreamException exception)
            {
                throw new InvalidDataException($"Sector {sectorId} has a truncated tile block.", exception);
            }

            return tiles;
        }
        finally
        {
            _wldxLock.Release();
        }
    }

    public async Task<byte[]> ReadWldx(
        KeyxSectorRecord entry,
        uint sectorId,
        CancellationToken cancellationToken = default)
    {
        var compressed = await ReadWldxCompressed(entry, sectorId, cancellationToken);

        byte[] decompressed;
        using (var compressedStream = new MemoryStream(compressed, writable: false))
        await using (var zlib = new ZLibStream(compressedStream, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            await zlib.CopyToAsync(output, cancellationToken);
            decompressed = output.ToArray();
        }

        if (entry.TilesRelativeOffset < 0 ||
            entry.TilesSize < SectorW * SectorH * WldxTileRecord.Size ||
            entry.TilesRelativeOffset + entry.TilesSize > decompressed.Length)
            throw new InvalidDataException($"Sector {sectorId} has an invalid tile block.");

        return decompressed;
    }

    private async Task<byte[]> ReadWldxCompressed(
        KeyxSectorRecord entry,
        uint sectorId,
        CancellationToken cancellationToken)
    {
        await _wldxLock.WaitAsync(cancellationToken);
        try
        {
            ValidateCompressedBlock(entry, sectorId);
            _wldxStream.Position = entry.CompressedOffset;

            var compressed = new byte[(int)entry.CompressedSize];
            await _wldxStream.ReadExactlyAsync(compressed, cancellationToken);
            return compressed;
        }
        finally
        {
            _wldxLock.Release();
        }
    }

    private void ValidateCompressedBlock(KeyxSectorRecord entry, uint sectorId)
    {
        if (entry.CompressedSize > int.MaxValue ||
            (ulong)entry.CompressedOffset + entry.CompressedSize > (ulong)_wldxStream.Length)
        {
            throw new InvalidDataException($"Sector {sectorId} has an invalid compressed block.");
        }
    }

    private static async Task SkipExactlyAsync(
        Stream stream,
        int byteCount,
        uint sectorId,
        CancellationToken cancellationToken)
    {
        var remaining = byteCount;
        var scratch = new byte[Math.Min(8192, Math.Max(1, remaining))];
        while (remaining > 0)
        {
            var read = await stream.ReadAsync(
                scratch.AsMemory(0, Math.Min(scratch.Length, remaining)),
                cancellationToken);
            if (read == 0)
                throw new InvalidDataException($"Sector {sectorId} ends before its tile block.");

            remaining -= read;
        }
    }

    public void Dispose()
    {
        _wldxStream.Dispose();
        _wldxLock.Dispose();
    }
}
