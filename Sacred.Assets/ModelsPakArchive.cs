using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Granny;

namespace Sacred.Assets;

public sealed class ModelsPakArchive : IDisposable
{
    private const int HeaderSize = 0x100;
    private const int DescriptorSize = 0x0C;
    private const int NameProbeLength = 0x40;
    private static readonly Encoding NameEncoding = Encoding.Latin1;

    private readonly string _path;
    private readonly FileStream _stream;
    private readonly SemaphoreSlim _streamLock = new(1, 1);
    private readonly FrozenDictionary<string, ModelPakRecord> _recordsByName;
    private bool _disposed;

    private ModelsPakArchive(string path, FileStream stream, Dictionary<string, ModelPakRecord> recordsByName)
    {
        _path = path;
        _stream = stream;
        _recordsByName = recordsByName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static ModelsPakArchive Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("models.pak was not found.", path);

        var stream = OpenArchiveStream(path);

        Span<byte> header = stackalloc byte[HeaderSize];
        stream.ReadExactly(header);

        var count = ReadEntryCount(header, stream.Length);
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

        var recordsByName = new Dictionary<string, ModelPakRecord>(StringComparer.OrdinalIgnoreCase);
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
            }

            if (!string.IsNullOrWhiteSpace(record.Name))
            {
                recordsByName.TryAdd(record.Name, record);
            }
        }

        return new ModelsPakArchive(path, stream, recordsByName);
    }

    public async Task<GrnAsset> LoadModelAsync(
        string modelName,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        CancellationToken cancellationToken = default)
    {
        var record = await FindRecordAsync(modelName, cancellationToken).ConfigureAwait(false);
        var payload = await ReadPayloadAsync(record, cancellationToken).ConfigureAwait(false);
        return GrnAssetLoader.LoadFromBytes(Path.GetFileNameWithoutExtension(modelName), payload, meshExtractionMode);
    }

    public async Task<GrnAsset> LoadCharacterModelAsync(
        string baseModelName,
        IReadOnlyList<string> attachmentModelNames,
        IReadOnlySet<string>? hiddenBaseTextureNames = null,
        CancellationToken cancellationToken = default)
    {
        var basePayload = await ReadPayloadAsync(
            await FindRecordAsync(baseModelName, cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        var attachmentPayloads = new byte[attachmentModelNames.Count][];
        for (var i = 0; i < attachmentModelNames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attachmentRecord = await FindRecordAsync(attachmentModelNames[i], cancellationToken).ConfigureAwait(false);
            attachmentPayloads[i] = await ReadPayloadAsync(
                attachmentRecord,
                cancellationToken).ConfigureAwait(false);
        }

        return GrnAssetLoader.LoadCharacterFromBytes(
            Path.GetFileNameWithoutExtension(baseModelName),
            basePayload,
            attachmentPayloads,
            hiddenBaseTextureNames);
    }

    private Task<ModelPakRecord> FindRecordAsync(string modelName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_recordsByName.TryGetValue(modelName, out var record))
            return Task.FromResult(record);

        throw new FileNotFoundException($"Model '{modelName}' was not found in models.pak.");
    }

    private async Task<byte[]> ReadPayloadAsync(ModelPakRecord record, CancellationToken cancellationToken)
    {
        var payload = new byte[record.Size];

        await _streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _stream.Position = record.Offset;
            await _stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _streamLock.Release();
        }

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

    private static FileStream OpenArchiveStream(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _stream.Dispose();
        _streamLock.Dispose();
    }
}
