using System;
using System.IO;

namespace Sacred.Assets;

public static class TgaTextureDecoder
{
    public static TextureAsset LoadFromBytes(ReadOnlySpan<byte> data, string logicalName)
    {
        if (data.Length < 18)
            throw new InvalidDataException("TGA header is truncated.");

        var idLength = data[0];
        var imageType = data[2];
        var width = data[12] | (data[13] << 8);
        var height = data[14] | (data[15] << 8);
        var bpp = data[16];
        var descriptor = data[17];

        if (imageType != 2)
            throw new NotSupportedException($"Only uncompressed true-color TGA is supported. Type={imageType}");
        if (bpp is not (24 or 32))
            throw new NotSupportedException($"Only 24/32-bit TGA is supported. Bpp={bpp}");

        var bytesPerPixel = bpp / 8;
        var pixelDataOffset = 18 + idLength;
        var sourceLength = checked(width * height * bytesPerPixel);
        if (pixelDataOffset + sourceLength > data.Length)
            throw new InvalidDataException("TGA pixel data is truncated.");

        var source = data.Slice(pixelDataOffset, sourceLength);
        var rgba = new byte[width * height * 4];
        var topOrigin = (descriptor & 0x20) != 0;
        for (var y = 0; y < height; y++)
        {
            var srcY = topOrigin ? y : height - 1 - y;
            for (var x = 0; x < width; x++)
            {
                var si = (srcY * width + x) * bytesPerPixel;
                var di = (y * width + x) * 4;
                rgba[di + 0] = source[si + 2];
                rgba[di + 1] = source[si + 1];
                rgba[di + 2] = source[si + 0];
                rgba[di + 3] = bytesPerPixel == 4 ? source[si + 3] : (byte)255;
            }
        }

        return new TextureAsset(logicalName, width, height, rgba);
    }
}
