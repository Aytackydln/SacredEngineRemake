using System.IO.Compression;
using System.Text;

namespace Sacred.Assets.Paks.Texture;

public sealed class TexturePakData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int TextureHeaderSize = 0x50;
    private static readonly Encoding NameEncoding = Encoding.Latin1;

    private readonly byte[] _data;
    private readonly Dictionary<string, TextureRecord> _recordsByName = new(StringComparer.OrdinalIgnoreCase);

    private TexturePakData(byte[] data)
    {
        _data = data;
        Index();
    }

    public static TexturePakData FromBytes(byte[] data) => new(data);

    public TextureAsset LoadTexture(string textureName)
    {
        if (!_recordsByName.TryGetValue(NormalizeName(textureName), out var record))
        {
            var stem = Path.GetFileNameWithoutExtension(textureName);
            if (stem is null || !_recordsByName.TryGetValue(stem, out record))
                throw new FileNotFoundException($"Texture '{textureName}' was not found in texture.pak.");
        }

        return new TextureAsset(record.Name, record.Width, record.Height, Decode(record));
    }

    private void Index()
    {
        if (_data.Length < HeaderSize)
            throw new InvalidDataException("texture.pak is too small to contain a header.");

        var count = PakDataHelpers.ReadEntryCount(_data, HeaderSize, DescriptorSize, "texture.pak");
        for (var i = 0; i < count; i++)
        {
            var descriptorOffset = HeaderSize + i * DescriptorSize;
            if (descriptorOffset + DescriptorSize > _data.Length)
                break;

            var offset = ReadUInt32(descriptorOffset + 4);
            var size = ReadUInt32(descriptorOffset + 8);
            if (offset <= 0 || size <= 0 || offset > int.MaxValue || size > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + TextureHeaderSize > _data.Length)
                continue;

            var name = PakDataHelpers.ReadCString(_data, recordOffset, 0x20, NameEncoding);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            AddLookupNames(new TextureRecord(
                name,
                recordOffset,
                (int)size,
                ReadUInt16(recordOffset + 0x20),
                ReadUInt16(recordOffset + 0x22),
                _data[recordOffset + 0x24]));
        }
    }

    private void AddLookupNames(TextureRecord record)
    {
        Add(record.Name, record);
        Add(Path.GetFileNameWithoutExtension(record.Name), record);

        var stem = Path.GetFileNameWithoutExtension(record.Name);
        if (stem?.StartsWith("mix", StringComparison.OrdinalIgnoreCase) == true)
            Add(stem + ".444", record);

        var lowerName = record.Name.ToLowerInvariant();
        if (lowerName.StartsWith("iso", StringComparison.Ordinal) &&
            lowerName.EndsWith(".tga", StringComparison.Ordinal) &&
            int.TryParse(lowerName[3..^4], out var number))
        {
            for (var width = 1; width <= 4; width++)
                Add($"iso{number.ToString().PadLeft(width, '0')}.tga", record);
            Add($"iso{number}.tga", record);
        }
    }

    private void Add(string? name, TextureRecord record)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _recordsByName[NormalizeName(name)] = record;
    }

    private byte[] Decode(TextureRecord record)
    {
        var payloadOffset = record.Offset + TextureHeaderSize;
        var payloadLength = Math.Min(record.Size, Math.Max(0, _data.Length - payloadOffset));
        var payload = _data.AsSpan(payloadOffset, payloadLength);

        return record.Type switch
        {
            0 => DecodeArgb4444(payload, record.Width, record.Height),
            3 => DecodeArgb4444(DecompressRle4444(payload, record.Width, record.Height), record.Width, record.Height),
            4 => DecodeArgb4444(Inflate(payload), record.Width, record.Height),
            6 => DecodeBgra(payload, record.Width, record.Height),
            _ => throw new NotSupportedException($"Unsupported texture type {record.Type} for '{record.Name}'.")
        };
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

    private ushort ReadUInt16(int offset) => BitConverter.ToUInt16(_data, offset);
    private uint ReadUInt32(int offset) => BitConverter.ToUInt32(_data, offset);
    private static string NormalizeName(string name) => name.Replace('\\', '/').Trim().ToLowerInvariant();

    private readonly record struct TextureRecord(string Name, int Offset, int Size, ushort Width, ushort Height, byte Type);
}