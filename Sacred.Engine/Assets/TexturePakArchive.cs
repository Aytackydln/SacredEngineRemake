using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Sacred.Core.Assets;

namespace Sacred.Engine.Assets;

public sealed class TexturePakArchive
{
    private static readonly Encoding NameEncoding = Encoding.Latin1;

    private readonly string _path;
    private readonly Dictionary<string, TexturePakRecord> _recordsByName = new(StringComparer.OrdinalIgnoreCase);

    private TexturePakArchive(string path)
    {
        _path = path;
        Index();
    }

    public static TexturePakArchive Load(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Texture PAK path cannot be empty.", nameof(path));

        return new TexturePakArchive(path);
    }

    public TextureAsset LoadTexture(string textureName)
    {
        if (!_recordsByName.TryGetValue(NormalizeName(textureName), out var record))
        {
            var stem = Path.GetFileNameWithoutExtension(textureName);
            var stemWithoutDimensions = StripDimensionSuffix(stem);
            if (!_recordsByName.TryGetValue(stem, out record) &&
                !_recordsByName.TryGetValue(stemWithoutDimensions, out record) &&
                !_recordsByName.TryGetValue(StripNumericSuffix(stemWithoutDimensions), out record))
                throw new FileNotFoundException($"Texture '{textureName}' was not found in texture.pak.");
        }

        using var stream = File.OpenRead(_path);
        var payloadOffset = record.Offset + TexturePakDecoder.TextureHeaderSize;
        var payloadLength = Math.Min(record.Size, Math.Max(0, stream.Length - payloadOffset));

        stream.Position = payloadOffset;
        var payload = new byte[(int)payloadLength];
        stream.ReadExactly(payload);
        return TexturePakDecoder.Decode(record, payload);
    }

    private void Index()
    {
        using var stream = File.OpenRead(_path);
        var header = new byte[TexturePakDecoder.HeaderSize];
        if (stream.Read(header) != header.Length)
            throw new InvalidDataException("texture.pak is too small to contain a header.");

        var count = TexturePakDecoder.ReadEntryCount(header, stream.Length);
        Span<byte> descriptor = stackalloc byte[TexturePakDecoder.DescriptorSize];
        Span<byte> textureHeader = stackalloc byte[TexturePakDecoder.TextureHeaderSize];

        for (var i = 0; i < count; i++)
        {
            stream.Position = TexturePakDecoder.HeaderSize + i * TexturePakDecoder.DescriptorSize;
            stream.ReadExactly(descriptor);

            var offset = BitConverter.ToUInt32(descriptor[4..8]);
            var size = BitConverter.ToUInt32(descriptor[8..12]);
            if (offset <= 0 || size <= 0 || offset > int.MaxValue || size > int.MaxValue)
                continue;

            if (offset + TexturePakDecoder.TextureHeaderSize > stream.Length)
                continue;

            stream.Position = offset;
            stream.ReadExactly(textureHeader);

            var name = ReadCString(textureHeader, 0x20);
            if (string.IsNullOrWhiteSpace(name))
                continue;

            AddLookupNames(new TexturePakRecord(
                name,
                offset,
                (int)size,
                BitConverter.ToUInt16(textureHeader[0x20..0x22]),
                BitConverter.ToUInt16(textureHeader[0x22..0x24]),
                textureHeader[0x24]));
        }
    }

    private void AddLookupNames(TexturePakRecord record)
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
                Add(number.ToString().PadLeft(width, '0').Insert(0, "iso") + ".tga", record);
            Add($"iso{number}.tga", record);
        }
    }

    private void Add(string? name, TexturePakRecord record)
    {
        if (!string.IsNullOrWhiteSpace(name))
            _recordsByName[NormalizeName(name)] = record;
    }

    private static string ReadCString(ReadOnlySpan<byte> data, int maxLength)
    {
        var length = 0;
        var limit = Math.Min(data.Length, maxLength);
        while (length < limit && data[length] != 0)
            length++;

        return NameEncoding.GetString(data[..length]);
    }

    private static string NormalizeName(string name) => name.Replace('\\', '/').Trim().ToLowerInvariant();

    private static string StripDimensionSuffix(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return string.Empty;

        var separator = stem.LastIndexOf('_');
        if (separator <= 0 || separator + 1 >= stem.Length)
            return stem;

        var suffix = stem[(separator + 1)..];
        var x = suffix.IndexOf('x');
        if (x <= 0 || x + 1 >= suffix.Length)
            return stem;

        return int.TryParse(suffix[..x], out _) && int.TryParse(suffix[(x + 1)..], out _)
            ? stem[..separator]
            : stem;
    }

    private static string StripNumericSuffix(string stem)
    {
        var separator = stem.LastIndexOf('_');
        if (separator <= 0 || separator + 1 >= stem.Length)
            return stem;

        return int.TryParse(stem[(separator + 1)..], out _)
            ? stem[..separator]
            : stem;
    }
}
