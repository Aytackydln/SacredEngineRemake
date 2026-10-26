using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Sacred.Assets.Utils;
using Sacred.Core.Pak.Models;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Loading;

namespace Sacred.Assets.Paks.Models;

public sealed class ModelsPakArchive : IDisposable
{
    private const int NameProbeLength = 0x40;
    private const int DefaultMotionReferenceOffset = 116;
    private const int ModelScaleOffset = 1136;
    private const int ModelScaleSize = 12;
    private static readonly Encoding NameEncoding = Encoding.Latin1;

    private readonly Stream _stream;
    private readonly SemaphoreSlim _streamLock = new(1, 1);
    private readonly FrozenDictionary<string, ModelPakRecord> _recordsByName;
    private readonly ModelsMetadataTable _metadata;
    private readonly SwappableGrnAssetLoader _assetLoader;
    private bool _disposed;

    private ModelsPakArchive(
        Stream stream,
        Dictionary<string, ModelPakRecord> recordsByName,
        ModelsMetadataTable metadata,
        IGrnAssetLoader assetLoader)
    {
        _stream = stream;
        _recordsByName = recordsByName.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
        _metadata = metadata;
        _assetLoader = new SwappableGrnAssetLoader(assetLoader);
    }

    public static ModelsPakArchive Load(
        string path,
        string? metadataPath = null,
        IGrnAssetLoader? assetLoader = null)
    {
        var stream = OpenArchiveStream(path);
        try
        {
            var metadata = metadataPath is not null && File.Exists(metadataPath)
                ? File.ReadAllBytes(metadataPath) : null;
            return LoadAsync(stream, metadata, assetLoader).GetAwaiter().GetResult();
        }
        catch { stream.Dispose(); throw; }
    }

    /// <summary>Indexes a seekable archive using asynchronous range reads. Takes ownership on success.</summary>
    public static async Task<ModelsPakArchive> LoadAsync(Stream stream, byte[]? metadataBytes = null,
        IGrnAssetLoader? assetLoader = null, CancellationToken cancellationToken = default)
    {
        if (!stream.CanRead || !stream.CanSeek) throw new ArgumentException("A readable seekable stream is required.", nameof(stream));
        stream.Position = 0;
        var headerBytes = new byte[ModelsPakHeaderLayout.SerializedSize];
        await stream.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        var header = MemoryMarshal.Read<ModelsPakHeaderLayout>(headerBytes);
        var count = checked((int)header.EntryCount);
        if (count > (stream.Length - stream.Position) / ModelPakDescriptor.SerializedSize)
            throw new InvalidDataException("Models.pak descriptor table exceeds the archive.");
        var descriptorBytes = new byte[checked(count * ModelPakDescriptor.SerializedSize)];
        await stream.ReadExactlyAsync(descriptorBytes, cancellationToken).ConfigureAwait(false);
        var descriptors = MemoryMarshal.Cast<byte, ModelPakDescriptor>(descriptorBytes).ToArray();

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
        var nameRanges = modelDescriptors.Select(descriptor => descriptor.Offset).Distinct()
            .Where(sizesByOffset.ContainsKey)
            .Select(offset => new StreamReadRange(offset, Math.Min(NameProbeLength, sizesByOffset[offset]))).ToArray();
        var nameBytes = await StreamRangeReader.ReadAsync(stream, nameRanges, cancellationToken).ConfigureAwait(false);
        var nameOffset = 0;
        foreach (var descriptor in modelDescriptors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sizesByOffset.TryGetValue(descriptor.Offset, out var size))
                continue;

            if (!recordsByOffset.TryGetValue(descriptor.Offset, out var record))
            {
                var probe = nameBytes.AsSpan(nameOffset, Math.Min(NameProbeLength, size));
                nameOffset += probe.Length;
                var end = probe.IndexOf((byte)0);
                var name = end <= 0 ? string.Empty : NameEncoding.GetString(probe[..end]);
                record = new ModelPakRecord(descriptor.Offset, size, name);
                recordsByOffset.Add(descriptor.Offset, record);
            }

            if (!string.IsNullOrWhiteSpace(record.Name))
            {
                recordsByName.TryAdd(record.Name, record);
            }
        }

        var metadata = metadataBytes is null ? ModelsMetadataTable.Empty : ModelsMetadataTable.Parse(metadataBytes);
        return new ModelsPakArchive(
            stream,
            recordsByName,
            metadata,
            assetLoader ?? ManagedGrnAssetLoader.Instance);
    }

    public void ReplaceAssetLoader(IGrnAssetLoader assetLoader)
    {
        ArgumentNullException.ThrowIfNull(assetLoader);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _assetLoader.Replace(assetLoader);
    }

    /// <summary>Gets the named GRN sequence in an authored cGrannyModelChunk motions[] slot.</summary>
    public bool TryGetModelMotionName(string modelName, byte motionSlot, out string name) =>
        _metadata.TryGetMotionName(modelName, motionSlot, out name);

    /// <summary>Checks whether the archive contains a named model or animation payload.</summary>
    public bool ContainsResource(string name) => _recordsByName.ContainsKey(name);

    public async Task<GrnAsset> LoadModelAsync(
        string modelName,
        GrnMeshExtractionMode meshExtractionMode = GrnMeshExtractionMode.PrimarySlice,
        CancellationToken cancellationToken = default)
    {
        var record = FindRecord(modelName, cancellationToken);
        var payload = await ReadPayloadAsync(record, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = ReadModelPayloadMetadata(payload);
        var asset = _assetLoader.LoadFromBytes(
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
        var clip = _assetLoader.TryExtractAnimation(
            animationPayload,
            motionName,
            ReadModelPayloadMetadata(metadataPrefix).Scale);
        return clip is null ? null : clip with { PlaybackSpeedMultiplier = _metadata.CharacterPlaybackSpeed(baseModelName, kind, weaponStyle) };
    }

    /// <summary>Loads an animation from the native per-model motions[] slot.</summary>
    public async Task<GrnAnimationClip?> LoadModelAnimationAsync(
        string modelName,
        byte motionSlot,
        CancellationToken cancellationToken = default)
    {
        var modelRecord = FindRecord(modelName, cancellationToken);
        var metadataPrefix = await ReadPayloadPrefixAsync(
            _stream,
            _streamLock,
            modelRecord,
            ModelScaleOffset + ModelScaleSize,
            cancellationToken).ConfigureAwait(false);
        if (!_metadata.TryGetMotionName(modelName, motionSlot, out var motionName) ||
            !_recordsByName.TryGetValue(motionName, out var animationRecord))
        {
            return null;
        }

        var animationPayload = await ReadPayloadAsync(
            _stream,
            _streamLock,
            animationRecord,
            cancellationToken).ConfigureAwait(false);
        var clip = _assetLoader.TryExtractAnimation(
            animationPayload,
            motionName,
            ReadModelPayloadMetadata(metadataPrefix).Scale);
        return clip is null ? null : clip with
        {
            PlaybackSpeedMultiplier = _metadata.ModelPlaybackSpeed(modelName, motionSlot),
            BowReleaseTailFraction = _metadata.BowReleaseTail(modelName, motionSlot)
        };
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
        var asset = _assetLoader.LoadCharacterFromBytes(
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
        var animation = _assetLoader.TryExtractAnimation(
            animationPayload,
            motionName,
            baseMetadata.Scale);
        cancellationToken.ThrowIfCancellationRequested();
        return animation is null ? null : animation with { PlaybackSpeedMultiplier = _metadata.MotionPlaybackSpeed(motionIndex) };
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
        Stream stream,
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
        Stream stream,
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

    private static FileStream OpenArchiveStream(string path) =>
        new(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, FileOptions.Asynchronous | FileOptions.RandomAccess);

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _assetLoader.Dispose();
        _stream.Dispose();
        _streamLock.Dispose();
    }
}

public readonly record struct ModelAttachmentReference(
    string ModelName,
    string? RigidAttachBoneName = null,
    string? SourceAttachBoneName = null);

internal readonly record struct ModelPayloadMetadata(Vector3 Scale, uint? DefaultMotionIndex);
