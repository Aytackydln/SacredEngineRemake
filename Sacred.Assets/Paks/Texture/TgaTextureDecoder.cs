using System.Buffers.Binary;
using System.IO.Compression;
using Sacred.Core.Pak.Texture;

namespace Sacred.Assets.Paks.Texture;

/// <summary>Decodes Sacred textures from unpacked TGA files or indexed Texture.pak payloads.</summary>
internal static class TgaTextureDecoder
{
    public static TextureAsset Decode(string name, ReadOnlySpan<byte> data)
    {
        if (data.Length < 18)
            throw new InvalidDataException($"TGA '{name}' has a truncated header.");

        var imageType = data[2];
        var width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..14]);
        var height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..16]);
        var depth = data[16];
        var descriptor = data[17];
        var grayscale = imageType is 3 or 11;
        var compressed = imageType is 10 or 11;
        if (data[1] != 0 || imageType is not (2 or 3 or 10 or 11) ||
            (grayscale ? depth is not (8 or 16) : depth is not (16 or 24 or 32)))
            throw new NotSupportedException($"TGA '{name}' has unsupported image type {imageType}, color map {data[1]}, or depth {depth}.");
        if (width == 0 || height == 0 || (descriptor & 0xC0) != 0)
            throw new InvalidDataException($"TGA '{name}' has invalid dimensions or interleaved pixels.");

        var pixelCount = checked(width * height);
        if (!compressed && !grayscale && depth is 24 or 32)
        {
            var offset = 18 + data[0];
            RequireBytes(data, offset, checked(pixelCount * (depth / 8)), name);
            return new TextureAsset(name, width, height,
                DecodeTrueColor(data[offset..], width, height, depth / 8, descriptor));
        }

        var rgba = new byte[checked(pixelCount * 4)];
        var source = 18 + data[0];
        var pixel = 0;
        var bytesPerPixel = depth / 8;
        while (pixel < pixelCount)
        {
            var count = 1;
            var repeated = false;
            if (compressed)
            {
                RequireBytes(data, source, 1, name);
                var packet = data[source++];
                count = (packet & 0x7F) + 1;
                repeated = (packet & 0x80) != 0;
            }

            if (count > pixelCount - pixel)
                throw new InvalidDataException($"TGA '{name}' has an RLE packet exceeding its pixel count.");
            RequireBytes(data, source, bytesPerPixel * (repeated ? 1 : count), name);
            for (var i = 0; i < count; i++, pixel++)
            {
                var x = pixel % width;
                var y = pixel / width;
                if ((descriptor & 0x10) != 0)
                    x = width - 1 - x;
                if ((descriptor & 0x20) == 0)
                    y = height - 1 - y;
                var destination = rgba.AsSpan((y * width + x) * 4, 4);
                ReadPixel(data.Slice(source, bytesPerPixel), destination, grayscale, descriptor);
                if (!repeated)
                    source += bytesPerPixel;
            }

            if (repeated)
                source += bytesPerPixel;
        }

        return new TextureAsset(name, width, height, rgba);
    }

    private static void ReadPixel(ReadOnlySpan<byte> source, Span<byte> rgba, bool grayscale, byte descriptor)
    {
        if (grayscale)
        {
            rgba[0] = rgba[1] = rgba[2] = source[0];
            rgba[3] = source.Length == 2 ? source[1] : byte.MaxValue;
        }
        else if (source.Length == 2)
        {
            var pixel = BinaryPrimitives.ReadUInt16LittleEndian(source);
            rgba[0] = Expand5((pixel >> 10) & 31);
            rgba[1] = Expand5((pixel >> 5) & 31);
            rgba[2] = Expand5(pixel & 31);
            rgba[3] = (descriptor & 0x0F) == 0 || (pixel & 0x8000) != 0 ? byte.MaxValue : (byte)0;
        }
        else
        {
            rgba[0] = source[2];
            rgba[1] = source[1];
            rgba[2] = source[0];
            rgba[3] = source.Length == 4 ? source[3] : byte.MaxValue;
        }
    }

    private static byte Expand5(int value) => (byte)((value << 3) | (value >> 2));

    private static void RequireBytes(ReadOnlySpan<byte> data, int offset, int count, string name)
    {
        if (offset > data.Length || count > data.Length - offset)
            throw new InvalidDataException($"TGA '{name}' has a truncated pixel block.");
    }

    public static TextureAsset Decode(TexturePakRecord record, ReadOnlySpan<byte> payload)
    {
        var rgba = record.StorageFormat switch
        {
            SacredTextureStorageFormat.Argb4444 =>
                DecodeArgb4444(payload, record.Width, record.Height),
            SacredTextureStorageFormat.RleArgb4444 =>
                DecodeArgb4444(
                    DecompressRle4444(payload, record.Width, record.Height),
                    record.Width,
                    record.Height),
            SacredTextureStorageFormat.ZlibArgb4444 =>
                DecodeArgb4444(Inflate(payload), record.Width, record.Height),
            SacredTextureStorageFormat.Bgra8888 =>
                DecodeBgra(payload, record.Width, record.Height),
            _ => throw new NotSupportedException($"Unsupported texture type {record.Type} for '{record.Name}'.")
        };

        return new TextureAsset(record.Name, record.Width, record.Height, rgba);
    }

    private static byte[] DecodeBgra(ReadOnlySpan<byte> source, int width, int height)
    {
        var rgba = new byte[checked(width * height * 4)];
        var sourceLength = Math.Min(source.Length, rgba.Length);
        for (var si = 0; si + 3 < sourceLength; si += 4)
        {
            rgba[si] = source[si + 2];
            rgba[si + 1] = source[si + 1];
            rgba[si + 2] = source[si];
            rgba[si + 3] = source[si + 3];
        }

        return rgba;
    }

    private static byte[] DecodeTrueColor(ReadOnlySpan<byte> source, int width, int height, int bytesPerPixel, byte descriptor)
    {
        if (bytesPerPixel == 4 && (descriptor & 0x30) == 0x20)
            return DecodeBgra(source, width, height);

        var rgba = new byte[checked(width * height * 4)];
        var topOrigin = (descriptor & 0x20) != 0;
        var rightOrigin = (descriptor & 0x10) != 0;
        var step = rightOrigin ? -bytesPerPixel : bytesPerPixel;
        for (var y = 0; y < height; y++)
        {
            var sourceY = topOrigin ? y : height - 1 - y;
            var si = (sourceY * width + (rightOrigin ? width - 1 : 0)) * bytesPerPixel;
            var end = (y + 1) * width * 4;
            if (bytesPerPixel == 4)
            {
                for (var di = y * width * 4; di < end; di += 4, si += step)
                {
                    rgba[di] = source[si + 2];
                    rgba[di + 1] = source[si + 1];
                    rgba[di + 2] = source[si];
                    rgba[di + 3] = source[si + 3];
                }
            }
            else
            {
                for (var di = y * width * 4; di < end; di += 4, si += step)
                {
                    rgba[di] = source[si + 2];
                    rgba[di + 1] = source[si + 1];
                    rgba[di + 2] = source[si];
                    rgba[di + 3] = byte.MaxValue;
                }
            }
        }
        return rgba;
    }

    private static byte[] DecodeArgb4444(ReadOnlySpan<byte> source, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var sourcePixels = Math.Min(source.Length / 2, width * height);
        for (var i = 0; i < sourcePixels; i++)
        {
            var value = source[i * 2] | (source[i * 2 + 1] << 8);
            var di = i * 4;
            rgba[di + 0] = (byte)(((value >> 8) & 0xF) * 17);
            rgba[di + 1] = (byte)(((value >> 4) & 0xF) * 17);
            rgba[di + 2] = (byte)((value & 0xF) * 17);
            rgba[di + 3] = (byte)(((value >> 12) & 0xF) * 17);
        }

        return rgba;
    }

    private static byte[] DecompressRle4444(ReadOnlySpan<byte> source, int width, int height)
    {
        var output = new byte[width * height * 2];
        var src = 0;
        var dst = 0;
        var bytesWritten = 0;
        var maxBytes = output.Length;

        while (dst + 1 < output.Length && src < source.Length)
        {
            var control = source[src++];
            var length = control & 0x7F;
            if (length == 0x7F)
            {
                if (src + 1 >= source.Length)
                    break;
                length = source[src] | (source[src + 1] << 8);
                src += 2;
            }

            bytesWritten += length * 2;
            if (length == 0 || bytesWritten > maxBytes)
                break;

            if ((control & 0x80) != 0)
            {
                if (src + 1 >= source.Length)
                    break;
                var lo = source[src++];
                var hi = source[src++];
                for (var i = 0; i < length && dst + 1 < output.Length; i++)
                {
                    output[dst++] = lo;
                    output[dst++] = hi;
                }
            }
            else
            {
                var byteLength = Math.Min(length * 2, Math.Min(source.Length - src, output.Length - dst));
                source.Slice(src, byteLength).CopyTo(output.AsSpan(dst, byteLength));
                src += byteLength;
                dst += byteLength;
            }
        }

        return output;
    }

    private static byte[] Inflate(ReadOnlySpan<byte> compressed)
    {
        using var compressedStream = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(compressedStream, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }
}
