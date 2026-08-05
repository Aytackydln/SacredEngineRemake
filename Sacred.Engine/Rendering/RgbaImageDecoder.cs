using System;
using System.Buffers.Binary;
using System.IO;

namespace Sacred.Engine.Rendering;

internal static class RgbaImageDecoder
{
    public static RgbaImage LoadBitmap(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 54 || bytes[0] != (byte)'B' || bytes[1] != (byte)'M')
            throw new InvalidDataException($"'{path}' is not a Windows bitmap.");

        var pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(10, 4));
        var dibSize = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(14, 4));
        var width = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(18, 4));
        var signedHeight = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(22, 4));
        var planes = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(26, 2));
        var bitsPerPixel = BinaryPrimitives.ReadInt16LittleEndian(bytes.AsSpan(28, 2));
        var compression = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(30, 4));
        if (dibSize < 40 || width <= 0 || signedHeight == 0 || planes != 1 ||
            bitsPerPixel is not (24 or 32) || compression != 0)
        {
            throw new NotSupportedException(
                $"Bitmap '{path}' must be an uncompressed 24-bit or 32-bit RGB image.");
        }

        var height = Math.Abs(signedHeight);
        var sourceRowBytes = checked(((width * bitsPerPixel + 31) / 32) * 4);
        var requiredLength = checked(pixelOffset + sourceRowBytes * height);
        if (pixelOffset < 0 || requiredLength > bytes.Length)
            throw new InvalidDataException($"Bitmap '{path}' has a truncated pixel block.");

        var rgba = new byte[checked(width * height * 4)];
        var bytesPerPixel = bitsPerPixel / 8;
        var topDown = signedHeight < 0;
        for (var y = 0; y < height; y++)
        {
            var sourceY = topDown ? y : height - 1 - y;
            var sourceRow = pixelOffset + sourceY * sourceRowBytes;
            var destinationRow = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                var source = sourceRow + x * bytesPerPixel;
                var destination = destinationRow + x * 4;
                rgba[destination] = bytes[source + 2];
                rgba[destination + 1] = bytes[source + 1];
                rgba[destination + 2] = bytes[source];
                rgba[destination + 3] = bytesPerPixel == 4 ? bytes[source + 3] : (byte)255;
            }
        }

        return new RgbaImage(width, height, rgba);
    }

    public static RgbaImage LoadTga(Stream stream, string name)
    {
        Span<byte> header = stackalloc byte[18];
        stream.ReadExactly(header);
        var idLength = header[0];
        var imageType = header[2];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(header[12..14]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(header[14..16]);
        var bitsPerPixel = header[16];
        var descriptor = header[17];
        if (imageType != 2 || bitsPerPixel is not (24 or 32) || width == 0 || height == 0)
        {
            throw new NotSupportedException(
                $"Embedded TGA '{name}' must be an uncompressed 24-bit or 32-bit true-color image.");
        }

        if (idLength != 0)
            stream.Seek(idLength, SeekOrigin.Current);

        var bytesPerPixel = bitsPerPixel / 8;
        var source = new byte[checked(width * height * bytesPerPixel)];
        stream.ReadExactly(source);
        var rgba = new byte[checked(width * height * 4)];
        var topOrigin = (descriptor & 0x20) != 0;
        var rightOrigin = (descriptor & 0x10) != 0;
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sourceX = rightOrigin ? width - 1 - x : x;
            var sourceY = topOrigin ? y : height - 1 - y;
            var sourceOffset = (sourceY * width + sourceX) * bytesPerPixel;
            var destinationOffset = (y * width + x) * 4;
            rgba[destinationOffset] = source[sourceOffset + 2];
            rgba[destinationOffset + 1] = source[sourceOffset + 1];
            rgba[destinationOffset + 2] = source[sourceOffset];
            rgba[destinationOffset + 3] = bytesPerPixel == 4 ? source[sourceOffset + 3] : (byte)255;
        }

        return new RgbaImage(width, height, rgba);
    }
}

internal sealed record RgbaImage(int Width, int Height, byte[] Rgba);
