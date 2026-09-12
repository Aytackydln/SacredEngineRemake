using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;

namespace Sacred.Engine.Graphics;

internal delegate void PngScanlineWriter(int rowIndex, Span<byte> destination);

/// <summary>Writes the PNG container shared by SDR and HDR screenshot encoders.</summary>
internal static class PngScreenshotEncoder
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static void WriteSignature(Stream output) => output.Write(Signature);

    public static void WriteHeader(
        Stream output,
        int width,
        int height,
        byte bitDepth,
        byte colorType)
    {
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = bitDepth;
        header[9] = colorType;
        WriteChunk(output, "IHDR"u8, header);
    }

    public static void WriteImageData(
        Stream output,
        int height,
        int rowByteCount,
        PngScanlineWriter writeScanline)
    {
        ArgumentNullException.ThrowIfNull(writeScanline);

        using var compressed = new MemoryStream();
        using (var compressor = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[checked(1 + rowByteCount)];
            for (var y = 0; y < height; y++)
            {
                row[0] = 0; // No PNG scanline filter.
                writeScanline(y, row.AsSpan(1));
                compressor.Write(row);
            }
        }

        if (!compressed.TryGetBuffer(out var bytes))
            throw new InvalidOperationException("Could not access compressed screenshot pixels.");
        WriteChunk(output, "IDAT"u8, bytes.AsSpan(0, checked((int)compressed.Length)));
    }

    public static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)data.Length));
        output.Write(length);
        output.Write(type);
        output.Write(data);

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, CalculateCrc(type, data));
        output.Write(checksum);
    }

    private static uint CalculateCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var crc = UpdateCrc(uint.MaxValue, type);
        return ~UpdateCrc(crc, data);
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }

        return crc;
    }
}
