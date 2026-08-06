using System.Buffers.Binary;
using Sacred.World.Rendering;

namespace Sacred.World.Renderer.Terminal;

internal static class BmpWriter
{
    private const int FileHeaderSize = 14;
    private const int InfoHeaderSize = 40;

    public static void Write(string path, RgbaImage image)
    {
        image.Validate();
        var pixelBytes = checked(image.Width * image.Height * 4);
        var pixelsOffset = FileHeaderSize + InfoHeaderSize;
        var fileSize = checked(pixelsOffset + pixelBytes);
        var header = new byte[pixelsOffset];
        header[0] = (byte)'B';
        header[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(2), fileSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(10), pixelsOffset);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(14), InfoHeaderSize);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(18), image.Width);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(22), image.Height);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(26), 1);
        BinaryPrimitives.WriteInt16LittleEndian(header.AsSpan(28), 32);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(34), pixelBytes);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        stream.Write(header);
        var row = new byte[checked(image.Width * 4)];
        for (var y = image.Height - 1; y >= 0; y--)
        {
            for (var x = 0; x < image.Width; x++)
            {
                var source = (y * image.Width + x) * 4;
                var destination = x * 4;
                row[destination] = image.Pixels[source + 2];
                row[destination + 1] = image.Pixels[source + 1];
                row[destination + 2] = image.Pixels[source];
                row[destination + 3] = 255;
            }
            stream.Write(row);
        }
    }
}
