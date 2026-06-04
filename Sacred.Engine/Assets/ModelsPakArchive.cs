using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Sacred.Core.Assets;

namespace Sacred.Engine.Assets;

public sealed class ModelsPakArchive
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int NameProbeLength = 0x40;
    private const int ScanChunkSize = 64 * 1024;
    private static readonly Encoding NameEncoding = Encoding.Latin1;

    private readonly string _path;

    private readonly List<ModelPakRecord> _records;
    private readonly Dictionary<string, ModelPakRecord> _recordsByName = new();
    private readonly Dictionary<uint, ModelPakRecord> _recordsById = new();

    private ModelsPakArchive(string path, List<ModelPakRecord> records)
    {
        _path = path;
        _records = records;
    }

    public static ModelsPakArchive Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("models.pak was not found.", path);

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[HeaderSize];
        stream.ReadExactly(header);

        var count = ReadEntryCount(header, stream.Length);
        var records = new List<ModelPakRecord>(count);
        var descriptors = new byte[count * DescriptorSize];
        stream.ReadExactly(descriptors);

        var modelDescriptors = new List<ModelPakDescriptor>(count);
        for (uint i = 0; i < count; i++)
        {
            var descriptorOffset = (int)i * DescriptorSize;
            var offset = BitConverter.ToUInt32(descriptors.AsSpan(descriptorOffset + 4, 4));
            if (offset > 0 && offset < stream.Length)
                modelDescriptors.Add(new ModelPakDescriptor(i, offset));
        }

        var orderedOffsets = modelDescriptors
            .Select(static descriptor => descriptor.Offset)
            .Distinct()
            .Order()
            .ToArray();
        var sizesByOffset = new Dictionary<uint, int>(orderedOffsets.Length);
        for (var i = 0; i < orderedOffsets.Length; i++)
        {
            var offset = orderedOffsets[i];
            var end = i + 1 < orderedOffsets.Length ? orderedOffsets[i + 1] : stream.Length;
            var size = end - offset;
            if (size is > 0 and <= int.MaxValue)
                sizesByOffset[offset] = (int)size;
        }

        var archive = new ModelsPakArchive(path, records);
        var recordsByOffset = new Dictionary<uint, ModelPakRecord>();
        foreach (var descriptor in modelDescriptors)
        {
            if (!sizesByOffset.TryGetValue(descriptor.Offset, out var size))
                continue;

            if (!recordsByOffset.TryGetValue(descriptor.Offset, out var record))
            {
                var name = ReadPayloadStartName(stream, descriptor.Offset, size);
                record = new ModelPakRecord(descriptor.Offset, size, name);
                recordsByOffset.Add(descriptor.Offset, record);
                records.Add(record);
            }

            if (!string.IsNullOrWhiteSpace(record.Name))
            {
                archive._recordsByName.TryAdd(record.Name, record);
                archive._recordsById.TryAdd(descriptor.EntryId, record);
            }
        }

        return archive;
    }

    public GrnAsset LoadModel(string modelName, GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice)
    {
        var record = FindRecord(modelName);
        var payload = ReadPayload(record);
        return GrnAssetLoader.LoadFromBytes(Path.GetFileNameWithoutExtension(modelName), payload, meshExtractionMode);
    }

    public GrnAsset LoadCharacterModel(
        string baseModelName,
        IReadOnlyList<string> attachmentModelNames,
        IReadOnlySet<string>? hiddenBaseTextureNames = null)
    {
        var basePayload = ReadPayload(FindRecord(baseModelName));
        var attachmentPayloads = attachmentModelNames
            .Select(name => ReadPayload(FindRecord(name)))
            .ToArray();

        return GrnAssetLoader.LoadCharacterFromBytes(
            Path.GetFileNameWithoutExtension(baseModelName),
            basePayload,
            attachmentPayloads,
            hiddenBaseTextureNames);
    }
    
    public GrnAsset LoadModel(uint entryId, GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice)
    {
        if (!_recordsById.TryGetValue(entryId, out var record))
            throw new FileNotFoundException($"Model with entry ID {entryId} was not found in models.pak.");

        var payload = ReadPayload(record);
        return GrnAssetLoader.LoadFromBytes($"Model_{entryId}", payload, meshExtractionMode);
    }

    private ModelPakRecord FindRecord(string modelName)
    {
        if (_recordsByName.TryGetValue(modelName, out var record))
            return record;

        record = FindContainingRecord(modelName)
            ?? throw new FileNotFoundException($"Model '{modelName}' was not found in models.pak.");
        _recordsByName.TryAdd(modelName, record);
        return record;
    }

    private ModelPakRecord? FindContainingRecord(string normalizedName)
    {
        var needle = NameEncoding.GetBytes(normalizedName);
        var buffer = new byte[ScanChunkSize + needle.Length];

        using var stream = File.OpenRead(_path);
        foreach (var record in _records)
        {
            stream.Position = record.Offset;
            var remaining = record.Size;
            var overlap = 0;

            while (remaining > 0)
            {
                var read = stream.Read(buffer, overlap, Math.Min(ScanChunkSize, remaining));
                if (read == 0)
                    break;

                var length = overlap + read;
                if (ContainsAsciiIgnoreCase(buffer, length, needle))
                    return record;

                overlap = Math.Min(needle.Length - 1, length);
                if (overlap > 0)
                    Buffer.BlockCopy(buffer, length - overlap, buffer, 0, overlap);

                remaining -= read;
            }
        }

        return null;
    }

    private byte[] ReadPayload(ModelPakRecord record)
    {
        using var stream = File.OpenRead(_path);
        var payload = new byte[record.Size];
        stream.Position = record.Offset;
        stream.ReadExactly(payload);
        return payload;
    }

    private static string ReadPayloadStartName(FileStream stream, uint offset, int size)
    {
        var length = Math.Min(NameProbeLength, size);
        Span<byte> probe = stackalloc byte[length];
        stream.Position = offset;
        stream.ReadExactly(probe);

        var end = probe.IndexOf((byte)0);
        if (end <= 0)
            return string.Empty;

        return NameEncoding.GetString(probe[..end]);
    }

    private static bool ContainsAsciiIgnoreCase(byte[] buffer, int length, byte[] needle)
    {
        if (needle.Length == 0 || length < needle.Length)
            return false;

        for (var i = 0; i <= length - needle.Length; i++)
        {
            var match = true;
            for (var n = 0; n < needle.Length; n++)
            {
                if (ToLowerAscii(buffer[i + n]) == ToLowerAscii(needle[n]))
                    continue;

                match = false;
                break;
            }

            if (match)
                return true;
        }

        return false;
    }

    private static byte ToLowerAscii(byte value) =>
        value is >= (byte)'A' and <= (byte)'Z' ? (byte)(value + 32) : value;

    private static int ReadEntryCount(ReadOnlySpan<byte> header, long archiveLength)
    {
        var count32 = BitConverter.ToUInt32(header.Slice(4, 4));
        var count16 = BitConverter.ToUInt16(header.Slice(4, 2));
        var maxDescriptorCount = Math.Max(0, (archiveLength - HeaderSize) / DescriptorSize);

        if (count32 <= maxDescriptorCount)
            return (int)count32;
        if (count16 <= maxDescriptorCount)
            return count16;

        throw new InvalidDataException($"Cannot determine models.pak entry count. count16={count16}, count32={count32}, max={maxDescriptorCount}");
    }

}
