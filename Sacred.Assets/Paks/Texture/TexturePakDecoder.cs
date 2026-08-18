using System.IO.Compression;
using Sacred.Core.Pak.Texture;

namespace Sacred.Assets.Paks.Texture;

public static class TexturePakDecoder
{
    public const int HeaderSize = 0x100;
    public const int DescriptorSize = 0x0C;
    public const int TextureHeaderSize = 0x50;

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

    public static int ReadEntryCount(ReadOnlySpan<byte> header, long archiveLength)
    {
        if (header.Length < HeaderSize)
            throw new InvalidDataException("texture.pak is too small to contain a header.");

        var count32 = BitConverter.ToUInt32(header.Slice(4, 4));
        var count16 = BitConverter.ToUInt16(header.Slice(4, 2));
        var maxDescriptorCount = Math.Max(0, (archiveLength - HeaderSize) / DescriptorSize);

        if (count32 <= maxDescriptorCount)
            return (int)count32;
        if (count16 <= maxDescriptorCount)
            return count16;

        throw new InvalidDataException($"Cannot determine texture.pak entry count. count16={count16}, count32={count32}, max={maxDescriptorCount}");
    }

    private static byte[] DecodeBgra(ReadOnlySpan<byte> source, int width, int height)
    {
        var rgba = new byte[width * height * 4];
        var sourceLength = Math.Min(source.Length, rgba.Length);
        for (var si = 0; si + 3 < sourceLength; si += 4)
        {
            rgba[si + 0] = source[si + 2];
            rgba[si + 1] = source[si + 1];
            rgba[si + 2] = source[si + 0];
            rgba[si + 3] = source[si + 3];
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
