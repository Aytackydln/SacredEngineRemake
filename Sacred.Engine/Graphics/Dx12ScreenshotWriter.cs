using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using Sacred.Engine.Graphics.Swapchain;

namespace Sacred.Engine.Graphics;

/// <summary>Routes swap-chain pixels to the native SDR or HDR screenshot format.</summary>
internal static class Dx12ScreenshotWriter
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static string CreatePath(
        string gameDirectory,
        string? label,
        Dx12ScreenshotImage image)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameDirectory);
        ArgumentNullException.ThrowIfNull(image);

        var directory = Path.Combine(gameDirectory, "Screenshots", "Remake");
        Directory.CreateDirectory(directory);
        var extension = IsHdr(image) ? ".jxr" : ".png";
        return Path.Combine(
            directory,
            $"{DateTime.Now:yyyyMMdd-HHmmssfff}{SanitizeLabel(label)}{extension}");
    }

    public static void Save(Dx12ScreenshotImage image, string path)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (IsHdr(image))
        {
            Dx12HdrScreenshotWriter.Save(image, path);
            return;
        }

        if (image.Format != Dx12SdrSwapChain.SdrBackBufferFormat ||
            image.ColorSpace != Dx12SdrSwapChain.SdrColorSpace)
            throw UnsupportedFormat(image);

        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        output.Write(PngSignature);
        WriteHeader(output, image.Width, image.Height);
        WriteSrgbProfile(output);
        WritePixels(output, image);
        WriteChunk(output, "IEND"u8, []);
    }

    public static string DescribeColorSpace(Dx12ScreenshotImage image) => image.ColorSpace switch
    {
        Dx12SdrSwapChain.SdrColorSpace => "SDR sRGB / Rec.709",
        Dx12HdrSwapChain.HdrColorSpace => "HDR linear scRGB JPEG XR",
        _ => image.ColorSpace.ToString()
    };

    private static void WriteHeader(Stream output, int width, int height)
    {
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)width));
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], checked((uint)height));
        header[8] = 8;
        header[9] = 2; // RGB
        WriteChunk(output, "IHDR"u8, header);
    }

    private static void WriteSrgbProfile(Stream output)
    {
        // Force the UNORM back-buffer bytes to sRGB. Without this chunk, readers can
        // interpret the values as linear and produce the classic washed-out capture.
        WriteChunk(output, "sRGB"u8, [0]); // Perceptual rendering intent.
    }

    private static void WritePixels(Stream output, Dx12ScreenshotImage image)
    {
        using var compressed = new MemoryStream();
        using (var compressor = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            var row = new byte[1 + image.Width * 3];
            for (var y = 0; y < image.Height; y++)
            {
                row[0] = 0; // No PNG scanline filter.
                CopySdrRow(image, y, row.AsSpan(1));
                compressor.Write(row);
            }
        }

        if (!compressed.TryGetBuffer(out var bytes))
            throw new InvalidOperationException("Could not access compressed screenshot pixels.");
        WriteChunk(output, "IDAT"u8, bytes.AsSpan(0, checked((int)compressed.Length)));
    }

    private static void CopySdrRow(Dx12ScreenshotImage image, int y, Span<byte> destination)
    {
        var source = image.Pixels.AsSpan(y * image.Width * 4, image.Width * 4);
        for (var x = 0; x < image.Width; x++)
        {
            destination[x * 3] = source[x * 4 + 2];
            destination[x * 3 + 1] = source[x * 4 + 1];
            destination[x * 3 + 2] = source[x * 4];
        }
    }

    private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
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

    private static bool IsHdr(Dx12ScreenshotImage image) =>
        image.Format == Dx12HdrSwapChain.HdrBackBufferFormat &&
        image.ColorSpace == Dx12HdrSwapChain.HdrColorSpace;

    private static NotSupportedException UnsupportedFormat(Dx12ScreenshotImage image) => new(
        $"Screenshot format {image.Format} in {image.ColorSpace} is not supported.");

    private static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars();
        var characters = label.Trim().ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (Array.IndexOf(invalid, characters[index]) >= 0)
                characters[index] = '_';
        }

        return "-" + new string(characters);
    }
}
