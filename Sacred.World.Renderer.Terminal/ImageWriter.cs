using System.Buffers.Binary;
using Sacred.World.Rendering;

namespace Sacred.World.Renderer.Terminal;

internal enum ImageFormat { Tga, Bmp }

internal static class ImageWriter
{
    public static string Extension(ImageFormat format) => format.ToString().ToLowerInvariant();

    public static void Write(string path, RgbaImage image, ImageFormat format)
    {
        image.Validate();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        if (format == ImageFormat.Bmp)
        {
            BmpWriter.Write(path, image);
            return;
        }
        if (image.Width > ushort.MaxValue || image.Height > ushort.MaxValue)
            throw new ArgumentException("TGA dimensions must be at most 65535 pixels.");
        Span<byte> header = stackalloc byte[18];
        header.Clear();
        header[2] = 2;
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)image.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)image.Height);
        header[16] = 32;
        // Sacred's loose-TGA loader always mirrors vertically, ignoring the
        // origin flag. Bottom-left storage also displays correctly in TGA editors.
        header[17] = 0x08; // Bottom-left origin, eight alpha bits.
        using var stream = File.Create(path);
        stream.Write(header);
        var row = new byte[image.Stride];
        for (var y = image.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var source = y * image.Stride + x * 4;
                var target = x * 4;
                row[target] = image.Pixels[source + 2];
                row[target + 1] = image.Pixels[source + 1];
                row[target + 2] = image.Pixels[source];
                row[target + 3] = image.Pixels[source + 3];
            }
            stream.Write(row);
        }
    }
}
