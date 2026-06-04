using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using static Sacred.Core.Assets.PakDataHelpers;

namespace Sacred.Core.Assets;

public sealed class FloorPakData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int RecordSize = 0x10;
    private const uint PrimaryTileMask = 0x1FFFF;
    private const int SecondaryTileShift = 17;
    private const uint SecondaryTileMask = 0x7FFF;

    private readonly Dictionary<uint, FloorOverlayRecord> _recordsById = new();

    private FloorPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Floor.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, DescriptorSize, "Floor.pak");
        for (uint floorId = 0; floorId < count; floorId++)
        {
            var descriptorOffset = HeaderSize + (int)floorId * DescriptorSize;
            if (descriptorOffset + DescriptorSize > data.Length)
                break;

            var offset = BitConverter.ToUInt32(data.Slice(descriptorOffset + 4, 4));
            if (floorId == 0 || offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + RecordSize > data.Length)
                continue;

            _recordsById[floorId] = new FloorOverlayRecord(
                floorId,
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x04, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x0C, 4)));
        }
    }

    public static FloorPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public FloorOverlayRecord? Get(uint floorId) =>
        _recordsById.TryGetValue(floorId, out var record) ? record : null;

    public static uint PrimaryTileId(uint tileOrBlendRef) => tileOrBlendRef & PrimaryTileMask;

    public static uint SecondaryTileId(uint tileOrBlendRef) =>
        (tileOrBlendRef >> SecondaryTileShift) & SecondaryTileMask;
}

public sealed class ItemsPakTypeData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int RecordSize = 0x80;

    private readonly Dictionary<uint, ItemTypeRecord> _records = new();

    private ItemsPakTypeData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Items.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, DescriptorSize, "Items.pak");
        for (uint typeId = 0; typeId < count; typeId++)
        {
            var descriptorOffset = HeaderSize + (int)typeId * DescriptorSize;
            if (descriptorOffset + DescriptorSize > data.Length)
                break;

            var descriptorType = BitConverter.ToUInt32(data.Slice(descriptorOffset, 4));
            var offset = BitConverter.ToUInt32(data.Slice(descriptorOffset + 4, 4));
            if (typeId == 0 || offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + RecordSize > data.Length)
                continue;

            _records[typeId] = new ItemTypeRecord(
                typeId,
                descriptorType,
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x10, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset, 4)),
                data[recordOffset + 0x2E]);
        }
    }

    public static ItemsPakTypeData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public ItemTypeRecord? Get(uint typeId) =>
        _records.TryGetValue(typeId, out var record) ? record : null;
}

public sealed class ItemsPakModelData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int RecordSize = 0x80;
    private const int ItemIdOffset = 0x20;
    private const int ModelNameOffset = 0x37;
    private const int ModelNameLength = 0x22;
    private static readonly Encoding NameEncoding = Encoding.Latin1;

    private readonly Dictionary<uint, PlayerCharacterItemRecord> _records = new();

    private ItemsPakModelData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Items.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, DescriptorSize, "Items.pak");
        for (uint entryId = 0; entryId < count; entryId++)
        {
            var descriptorOffset = HeaderSize + (int)entryId * DescriptorSize;
            if (descriptorOffset + DescriptorSize > data.Length)
                break;

            var offset = BitConverter.ToUInt32(data.Slice(descriptorOffset + 4, 4));
            if (entryId == 0 || offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + RecordSize > data.Length)
                continue;

            var modelName = ReadCString(data, recordOffset + ModelNameOffset, ModelNameLength, NameEncoding);
            if (string.IsNullOrWhiteSpace(modelName))
                continue;

            var itemId = BitConverter.ToUInt32(data.Slice(recordOffset + ItemIdOffset, 4));
            _records[entryId] = new PlayerCharacterItemRecord(
                entryId,
                itemId,
                modelName);
        }
    }

    public static ItemsPakModelData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public PlayerCharacterItemRecord? Get(uint entryId) =>
        _records.TryGetValue(entryId, out var record) ? record : null;
}

public sealed class MixedPakData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private static readonly Encoding NameEncoding = Encoding.ASCII;

    private readonly Dictionary<uint, List<MixedCutoutRecord>> _groups = new();
    private readonly Dictionary<uint, uint> _cutoutIdToGroup = new();

    private MixedPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Mixed.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, DescriptorSize, "Mixed.pak");
        for (uint mixedId = 0; mixedId < count; mixedId++)
        {
            var descriptorOffset = HeaderSize + (int)mixedId * DescriptorSize;
            if (descriptorOffset + DescriptorSize > data.Length)
                break;

            var offset = BitConverter.ToUInt32(data.Slice(descriptorOffset + 4, 4));
            var size = BitConverter.ToUInt32(data.Slice(descriptorOffset + 8, 4));
            if (offset == 0 || size <= 0x10 || offset > int.MaxValue || size > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            var recordSize = (int)size;
            if (recordOffset + recordSize > data.Length)
                continue;

            var pieceCount = Math.Min(
                BitConverter.ToUInt32(data.Slice(recordOffset, 4)),
                (uint)Math.Max(0, (recordSize - 0x10) / 0x40));
            if (pieceCount == 0)
                continue;

            var pieces = new List<MixedCutoutRecord>((int)pieceCount);
            var pieceOffset = recordOffset + 0x10;
            for (uint pieceIndex = 0; pieceIndex < pieceCount; pieceIndex++)
            {
                if (pieceOffset + 0x40 > data.Length)
                    break;

                var name = ReadCString(data, pieceOffset, 0x20, NameEncoding);
                var rec = pieceOffset + 0x20;
                var piece = new MixedCutoutRecord(
                    mixedId,
                    pieceIndex,
                    name,
                    BitConverter.ToUInt32(data.Slice(rec, 4)),
                    BitConverter.ToUInt16(data.Slice(rec + 0x04, 2)),
                    BitConverter.ToUInt16(data.Slice(rec + 0x06, 2)),
                    BitConverter.ToInt16(data.Slice(rec + 0x08, 2)),
                    BitConverter.ToInt16(data.Slice(rec + 0x0A, 2)),
                    BitConverter.ToSingle(data.Slice(rec + 0x10, 4)),
                    BitConverter.ToSingle(data.Slice(rec + 0x14, 4)),
                    BitConverter.ToSingle(data.Slice(rec + 0x18, 4)),
                    BitConverter.ToSingle(data.Slice(rec + 0x1C, 4)));
                pieces.Add(piece);
                _cutoutIdToGroup.TryAdd(piece.CutoutId, mixedId);
                pieceOffset += 0x40;
            }

            if (pieces.Count > 0)
                _groups[mixedId] = pieces;
        }
    }

    public static MixedPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public IReadOnlyList<MixedCutoutRecord>? GetGroup(uint groupId) =>
        _groups.GetValueOrDefault(groupId);

    public uint? ResolveGroupId(uint referenceId)
    {
        if (_groups.ContainsKey(referenceId))
            return referenceId;

        return _cutoutIdToGroup.TryGetValue(referenceId, out var groupId)
            ? groupId
            : null;
    }
}

public sealed class StaticPakData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int RecordSize = 0x40;

    private readonly Dictionary<uint, StaticObjectRecord> _recordsById = new();

    private StaticPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("Static.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, DescriptorSize, "Static.pak");
        for (uint staticId = 0; staticId < count; staticId++)
        {
            var descriptorOffset = HeaderSize + (int)staticId * DescriptorSize;
            if (descriptorOffset + DescriptorSize > data.Length)
                break;

            var descriptorType = BitConverter.ToUInt32(data.Slice(descriptorOffset, 4));
            var offset = BitConverter.ToUInt32(data.Slice(descriptorOffset + 4, 4));
            var size = BitConverter.ToUInt32(data.Slice(descriptorOffset + 8, 4));
            if (staticId == 0 || offset == 0 || offset > int.MaxValue)
                continue;

            var recordOffset = (int)offset;
            if (recordOffset + RecordSize > data.Length)
                continue;

            _recordsById[staticId] = new StaticObjectRecord(
                staticId,
                descriptorType,
                size,
                BitConverter.ToUInt32(data.Slice(recordOffset, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x04, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x08, 4)),
                BitConverter.ToUInt16(data.Slice(recordOffset + 0x0C, 2)),
                BitConverter.ToInt32(data.Slice(recordOffset + 0x0E, 4)),
                BitConverter.ToInt32(data.Slice(recordOffset + 0x12, 4)),
                BitConverter.ToUInt32(data.Slice(recordOffset + 0x1F, 4)),
                BitConverter.ToInt16(data.Slice(recordOffset + 0x2B, 2)),
                data[recordOffset + 0x2E],
                data[recordOffset + 0x2F],
                data[recordOffset + 0x30],
                data[recordOffset + 0x33]);
        }
    }

    public static StaticPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public StaticObjectRecord? Get(uint staticId) =>
        _recordsById.TryGetValue(staticId, out var record) ? record : null;
}

public sealed class TilesPakData
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private static readonly Encoding NameEncoding = Encoding.ASCII;

    private readonly List<TileDefinition> _definitions = [];

    private TilesPakData(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize)
            throw new InvalidDataException("tiles.pak is too small to contain a header.");

        var count = ReadEntryCount(data, HeaderSize, DescriptorSize, "tiles.pak");
        for (var i = 0; i < count; i++)
        {
            var descriptorOffset = HeaderSize + i * DescriptorSize;
            if (descriptorOffset + DescriptorSize > data.Length)
                break;

            var offset = BitConverter.ToUInt32(data.Slice(descriptorOffset + 4, 4));
            var size = BitConverter.ToUInt32(data.Slice(descriptorOffset + 8, 4));
            if (offset <= 0 || size <= 0 || offset > int.MaxValue || size > int.MaxValue)
            {
                _definitions.Add(TileDefinition.Empty);
                continue;
            }

            var recordOffset = (int)offset;
            var recordSize = (int)size;
            if (recordOffset + recordSize > data.Length)
            {
                _definitions.Add(TileDefinition.Empty);
                continue;
            }

            var fileName = ReadCString(data, recordOffset, 0x20, NameEncoding);
            var tileNumber = recordSize >= 0x28 ? BitConverter.ToUInt32(data.Slice(recordOffset + 0x24, 4)) : 0;
            _definitions.Add(new TileDefinition(fileName, tileNumber));
        }
    }

    public static TilesPakData FromBytes(ReadOnlySpan<byte> data) => new(data);

    public TileDefinition? Get(uint tileId) =>
        tileId <= int.MaxValue && (int)tileId < _definitions.Count
            ? _definitions[(int)tileId]
            : null;
}

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

        var count = ReadEntryCount(_data, HeaderSize, DescriptorSize, "texture.pak");
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

            var name = ReadCString(_data, recordOffset, 0x20, NameEncoding);
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

public static class TexturePakDecoder
{
    public const int HeaderSize = 0x100;
    public const int DescriptorSize = 0x0C;
    public const int TextureHeaderSize = 0x50;

    public static TextureAsset Decode(TexturePakRecord record, ReadOnlySpan<byte> payload)
    {
        var rgba = record.Type switch
        {
            0 => DecodeArgb4444(payload, record.Width, record.Height),
            3 => DecodeArgb4444(DecompressRle4444(payload, record.Width, record.Height), record.Width, record.Height),
            4 => DecodeArgb4444(Inflate(payload), record.Width, record.Height),
            6 => DecodeBgra(payload, record.Width, record.Height),
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

internal static class PakDataHelpers
{
    internal static int ReadEntryCount(ReadOnlySpan<byte> data, int headerSize, int descriptorSize, string archiveName)
    {
        var count32 = BitConverter.ToUInt32(data.Slice(4, 4));
        var count16 = BitConverter.ToUInt16(data.Slice(4, 2));
        var maxDescriptorCount = Math.Max(0, (data.Length - headerSize) / descriptorSize);

        if (count32 <= maxDescriptorCount)
            return (int)count32;
        if (count16 <= maxDescriptorCount)
            return count16;

        throw new InvalidDataException($"Cannot determine {archiveName} entry count. count16={count16}, count32={count32}, max={maxDescriptorCount}");
    }

    internal static string ReadCString(ReadOnlySpan<byte> data, int offset, int maxLength, Encoding encoding)
    {
        var end = offset;
        var maxEnd = Math.Min(data.Length, offset + maxLength);
        while (end < maxEnd && data[end] != 0)
            end++;

        return encoding.GetString(data.Slice(offset, end - offset));
    }
}
