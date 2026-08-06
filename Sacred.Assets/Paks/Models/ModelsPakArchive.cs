using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Sacred.Assets.Utils;
using Sacred.Granny;

namespace Sacred.Assets.Paks.Models;

public sealed class ModelsPakArchive : IDisposable
{
    private const int HeaderSize = 0x100;
    private const int NameProbeLength = 0x40;
    private const int DefaultMotionReferenceOffset = 116;
    private const int ModelScaleOffset = 1136;
    private const int ModelScaleSize = 12;
    private static readonly Encoding NameEncoding = Encoding.Latin1;

    private readonly FileStream _stream;
    private readonly SemaphoreSlim _streamLock = new(1, 1);
    private readonly FrozenDictionary<string, ModelPakRecord> _recordsByName;
    private readonly ModelsMetadataTable _metadata;
    private bool _disposed;

    private ModelsPakArchive(
        FileStream stream,
        Dictionary<string, ModelPakRecord> recordsByName,
        ModelsMetadataTable metadata)
    {
        _stream = stream;
        _recordsByName = recordsByName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _metadata = metadata;
    }

    public static ModelsPakArchive Load(string path, string? metadataPath = null)
    {
        using var stopwatch = new LoggingStopwatch("Loading Models.pak... ");

        if (!File.Exists(path))
            throw new FileNotFoundException("models.pak was not found.", path);

        var stream = OpenArchiveStream(path);

        Span<byte> header = stackalloc byte[HeaderSize];
        stream.ReadExactly(header);

        var count = ReadEntryCount(header, stream.Length);
        var descriptors = ReadDescriptors(stream, count);

        var modelDescriptors = new List<ModelPakDescriptor>(count);
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Offset > 0 && descriptor.Offset < stream.Length)
                modelDescriptors.Add(descriptor);
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

        var metadata = metadataPath is null
            ? ModelsMetadataTable.Empty
            : ModelsMetadataTable.Load(metadataPath);
        return new ModelsPakArchive(stream, recordsByName, metadata);
    }

    public async Task<GrnAsset> LoadModelAsync(
        string modelName,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        CancellationToken cancellationToken = default)
    {
        var record = FindRecord(modelName, cancellationToken);
        var payload = await ReadPayloadAsync(record, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = ReadModelPayloadMetadata(payload);
        var asset = GrnAssetLoader.LoadFromBytes(
            Path.GetFileNameWithoutExtension(modelName),
            payload,
            meshExtractionMode,
            metadata.Scale);
        cancellationToken.ThrowIfCancellationRequested();
        return asset;
    }

    public Task<GrnAsset> LoadCharacterModelAsync(
        string baseModelName,
        IReadOnlyList<ModelAttachmentReference> attachments,
        CancellationToken cancellationToken = default) =>
        LoadCharacterModelAsync(baseModelName, attachments, loadDefaultAnimation: true, cancellationToken);

    public Task<GrnAsset> LoadCharacterBaseModelAsync(
        string baseModelName,
        IReadOnlyList<ModelAttachmentReference> attachments,
        CancellationToken cancellationToken = default) =>
        LoadCharacterModelAsync(baseModelName, attachments, loadDefaultAnimation: false, cancellationToken);

    public async Task<GrnAnimationClip?> LoadDefaultCharacterAnimationAsync(
        string baseModelName,
        CancellationToken cancellationToken = default)
    {
        var baseRecord = FindRecord(baseModelName, cancellationToken);
        var metadataPrefix = await ReadPayloadPrefixAsync(
            _stream,
            _streamLock,
            baseRecord,
            ModelScaleOffset + ModelScaleSize,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return await LoadDefaultCharacterAnimationAsync(
            ReadModelPayloadMetadata(metadataPrefix),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<GrnAnimationClip?> LoadCharacterAnimationAsync(
        string baseModelName,
        CharacterMotionKind kind,
        CharacterMotionWeaponStyle weaponStyle,
        CancellationToken cancellationToken = default)
    {
        if (!_metadata.TryGetMotionName(baseModelName, kind, weaponStyle, out var motionName) ||
            !_recordsByName.TryGetValue(motionName, out var animationRecord))
        {
            return null;
        }

        var baseRecord = FindRecord(baseModelName, cancellationToken);
        var metadataPrefix = await ReadPayloadPrefixAsync(
            _stream,
            _streamLock,
            baseRecord,
            ModelScaleOffset + ModelScaleSize,
            cancellationToken).ConfigureAwait(false);
        var animationPayload = await ReadPayloadAsync(
            _stream,
            _streamLock,
            animationRecord,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return Granny1MeshExtractor.TryExtractAnimation(
            animationPayload,
            motionName,
            ReadModelPayloadMetadata(metadataPrefix).Scale);
    }

    private async Task<GrnAsset> LoadCharacterModelAsync(
        string baseModelName,
        IReadOnlyList<ModelAttachmentReference> attachments,
        bool loadDefaultAnimation,
        CancellationToken cancellationToken)
    {
        var basePayload = await ReadPayloadAsync(
            FindRecord(baseModelName, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var baseMetadata = ReadModelPayloadMetadata(basePayload);

        var attachmentPayloads = new GrnCharacterAttachment[attachments.Count];
        for (var i = 0; i < attachments.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var attachment = attachments[i];
            var attachmentRecord = FindRecord(attachment.ModelName, cancellationToken);
            var attachmentPayload = await ReadPayloadAsync(
                attachmentRecord,
                cancellationToken).ConfigureAwait(false);
            attachmentPayloads[i] = new GrnCharacterAttachment(
                attachmentPayload,
                attachment.RigidAttachBoneName,
                attachment.SourceAttachBoneName,
                ReadModelPayloadMetadata(attachmentPayload).Scale);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var asset = GrnAssetLoader.LoadCharacterFromBytes(
            Path.GetFileNameWithoutExtension(baseModelName),
            basePayload,
            attachmentPayloads,
            baseModelScale: baseMetadata.Scale);
        cancellationToken.ThrowIfCancellationRequested();
        if (!loadDefaultAnimation)
            return asset;

        return asset with
        {
            DefaultAnimation = await LoadDefaultCharacterAnimationAsync(baseMetadata, cancellationToken)
        };
    }

    private async Task<GrnAnimationClip?> LoadDefaultCharacterAnimationAsync(
        ModelPayloadMetadata baseMetadata,
        CancellationToken cancellationToken)
    {
        if (baseMetadata.DefaultMotionIndex is not { } motionIndex || motionIndex == 0 ||
            !_metadata.TryGetMotionName(motionIndex, out var motionName) ||
            !_recordsByName.TryGetValue(motionName, out var animationRecord))
        {
            return null;
        }

        var animationPayload = await ReadPayloadAsync(
            _stream,
            _streamLock,
            animationRecord,
            cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var animation = Granny1MeshExtractor.TryExtractAnimation(
            animationPayload,
            motionName,
            baseMetadata.Scale);
        cancellationToken.ThrowIfCancellationRequested();
        return animation;
    }

    private static ModelPayloadMetadata ReadModelPayloadMetadata(ReadOnlySpan<byte> payload)
    {
        var scale = Vector3.One;
        if (payload.Length >= ModelScaleOffset + ModelScaleSize)
        {
            scale = new Vector3(
                BitConverter.ToSingle(payload.Slice(ModelScaleOffset, 4)),
                BitConverter.ToSingle(payload.Slice(ModelScaleOffset + 4, 4)),
                BitConverter.ToSingle(payload.Slice(ModelScaleOffset + 8, 4)));
            if (!float.IsFinite(scale.X) || !float.IsFinite(scale.Y) || !float.IsFinite(scale.Z) ||
                MathF.Abs(scale.X) <= 0.000001f || MathF.Abs(scale.Y) <= 0.000001f || MathF.Abs(scale.Z) <= 0.000001f)
            {
                scale = Vector3.One;
            }
        }

        var motionIndex = payload.Length >= DefaultMotionReferenceOffset + 4
            ? BitConverter.ToUInt32(payload.Slice(DefaultMotionReferenceOffset, 4))
            : (uint?)null;
        return new ModelPayloadMetadata(scale, motionIndex);
    }

    private ModelPakRecord FindRecord(string modelName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_recordsByName.TryGetValue(modelName, out var record))
            return record;

        throw new FileNotFoundException($"Model '{modelName}' was not found in models.pak.");
    }

    private Task<byte[]> ReadPayloadAsync(ModelPakRecord record, CancellationToken cancellationToken) =>
        ReadPayloadAsync(_stream, _streamLock, record, cancellationToken);

    private static async Task<byte[]> ReadPayloadAsync(
        FileStream stream,
        SemaphoreSlim streamLock,
        ModelPakRecord record,
        CancellationToken cancellationToken)
    {
        var payload = new byte[record.Size];

        await streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            stream.Position = record.Offset;
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            streamLock.Release();
        }

        return payload;
    }

    private static async Task<byte[]> ReadPayloadPrefixAsync(
        FileStream stream,
        SemaphoreSlim streamLock,
        ModelPakRecord record,
        int requestedLength,
        CancellationToken cancellationToken)
    {
        var payload = new byte[Math.Min(record.Size, requestedLength)];

        await streamLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            stream.Position = record.Offset;
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            streamLock.Release();
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

    private static ModelPakDescriptor[] ReadDescriptors(FileStream stream, int count)
    {
        var descriptors = new ModelPakDescriptor[count];
        var descriptorBytes = MemoryMarshal.AsBytes(descriptors.AsSpan());
        var expectedLength = count * ModelPakDescriptor.SerializedSize;
        if (descriptorBytes.Length != expectedLength)
            throw new InvalidDataException($"models.pak descriptor layout is {descriptorBytes.Length / Math.Max(1, count)} bytes, expected {ModelPakDescriptor.SerializedSize}.");

        stream.ReadExactly(descriptorBytes);
        return descriptors;
    }

    private static int ReadEntryCount(ReadOnlySpan<byte> header, long archiveLength)
    {
        var count32 = BitConverter.ToUInt32(header.Slice(4, 4));
        var count16 = BitConverter.ToUInt16(header.Slice(4, 2));
        var maxDescriptorCount = Math.Max(0, (archiveLength - HeaderSize) / ModelPakDescriptor.SerializedSize);

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

public readonly record struct ModelAttachmentReference(
    string ModelName,
    string? RigidAttachBoneName = null,
    string? SourceAttachBoneName = null);

internal readonly record struct ModelPayloadMetadata(Vector3 Scale, uint? DefaultMotionIndex);
