using System.Numerics;

namespace Sacred.Granny;

public static partial class Granny1MeshExtractor
{
    private const uint AnimationTransformTrackChunk = 0xCA5E1204;
    private const int AnimationTrackHeaderSize = 52;
    private const int MaximumAnimationKeysPerChannel = 1_000_000;

    public static GrnAnimationClip? TryExtractAnimation(
        ReadOnlySpan<byte> data,
        string? name = null,
        Vector3? modelScale = null)
    {
        GrnAnimationClip? best = null;
        var sliceStarts = FindGrannySliceStarts(data);
        foreach (var (start, end) in EnumerateSlices(sliceStarts, data.Length))
        {
            var slice = data[start..end];
            var candidate = TryExtractAnimationSlice(
                slice,
                name ?? "Animation",
                SanitizeModelScale(modelScale ?? Vector3.One));
            if (candidate is not null &&
                (best is null || CountTracks(candidate) > CountTracks(best)))
                best = candidate;
        }

        return best;
    }

    private static GrnAnimationClip? TryExtractAnimationSlice(
        ReadOnlySpan<byte> data,
        string name,
        Vector3 modelScale)
    {
        if (data.Length < HeaderSize + 8 || ReadUInt32(data, HeaderSize) != MainChunk)
            return null;

        var mainOffset = HeaderSize + 4;
        var childCount = ReadUInt32(data, mainOffset);
        if (childCount == 0 || childCount > 16)
            return null;

        var position = mainOffset + 4 + 24;
        GrnAnimationClip? best = null;
        for (var child = 0; child < childCount; child++)
        {
            if (position + 20 > data.Length)
                break;

            var chunk = ReadUInt32(data, position);
            var listOffsetValue = ReadUInt32(data, position + 8);
            position += 20;
            if (chunk != ObjectChunk || listOffsetValue > int.MaxValue)
                continue;

            var listOffset = checked((int)listOffsetValue);
            if (listOffset < 0 || listOffset + ItemListHeaderSize > data.Length)
                continue;

            var descriptors = ReadItemDescriptors(data, listOffset, data.Length);
            var skeleton = ReadSkeleton(data, data.Length, descriptors);
            if (skeleton is null)
                continue;
            if (modelScale != Vector3.One)
                skeleton = ApplyModelScale(skeleton, modelScale);

            var textEntries = ReadTextEntries(data, data.Length, descriptors);
            var objects = ReadObjects(data, descriptors);
            var objectNameKey = FindStringIndex(textEntries, "__ObjectName");
            var transformChannelObjectIds = ReadBoneObjectIds(data, descriptors);
            var tracks = new GrnTransformTrack?[skeleton.Bones.Length];
            var duration = 0.0f;
            var trackDescriptors = descriptors
                .Where(static descriptor => descriptor.Chunk == AnimationTransformTrackChunk)
                .ToArray();
            for (var trackIndex = 0; trackIndex < trackDescriptors.Length; trackIndex++)
            {
                var descriptor = trackDescriptors[trackIndex];
                var nextDataOffset = trackDescriptors
                    .Select(static candidate => candidate.DataOffset)
                    .Where(candidate => candidate > descriptor.DataOffset)
                    .DefaultIfEmpty(data.Length)
                    .Min();
                if (!TryReadAnimationTrack(
                        data,
                        descriptor.DataOffset,
                        nextDataOffset,
                        modelScale,
                        out var channelId,
                        out var track,
                        out var trackDuration))
                    continue;

                var boneName = ResolveTransformChannelName(
                    channelId,
                    objectNameKey,
                    textEntries,
                    objects,
                    transformChannelObjectIds);
                if (string.IsNullOrWhiteSpace(boneName) ||
                    !skeleton.BonesByName.TryGetValue(boneName, out var boneIndex))
                    continue;

                tracks[boneIndex] ??= track;
                duration = MathF.Max(duration, trackDuration);
            }

            if (tracks.All(static track => track is null))
                continue;

            var publicSkeleton = new GrnSkeleton(skeleton.Bones.Select(static bone => new GrnBone(
                bone.Name,
                bone.ParentIndex,
                bone.RestTranslation,
                bone.RestRotation,
                bone.RestScaleShear,
                bone.RestLocal,
                bone.RestWorld)).ToArray());
            var candidate = new GrnAnimationClip(name, publicSkeleton, tracks, duration);
            if (best is null || CountTracks(candidate) > CountTracks(best))
                best = candidate;
        }

        return best;
    }

    private static bool TryReadAnimationTrack(
        ReadOnlySpan<byte> data,
        int offset,
        int nextOffset,
        Vector3 modelScale,
        out uint channelId,
        out GrnTransformTrack track,
        out float duration)
    {
        channelId = 0;
        track = null!;
        duration = 0.0f;
        if (offset < 0 || offset + AnimationTrackHeaderSize > data.Length)
            return false;

        channelId = ReadUInt32(data, offset);
        if (channelId == 0)
            return false;

        if (ReadUInt32(data, offset + 8) == 0)
            return TryReadInterleavedAnimationTrack(
                data,
                offset,
                nextOffset,
                modelScale,
                channelId,
                out track,
                out duration);

        var translationCount = ReadAnimationKeyCount(data, offset + 24);
        var rotationCount = ReadAnimationKeyCount(data, offset + 28);
        var scaleShearCount = ReadAnimationKeyCount(data, offset + 32);
        if (translationCount < 0 || rotationCount < 0 || scaleShearCount < 0)
            return false;

        var timelineOffset = (long)offset + AnimationTrackHeaderSize;
        var translationTimeOffset = timelineOffset;
        var rotationTimeOffset = translationTimeOffset + translationCount * 4L;
        var scaleShearTimeOffset = rotationTimeOffset + rotationCount * 4L;
        var translationOffset = scaleShearTimeOffset + scaleShearCount * 4L;
        var rotationOffset = translationOffset + translationCount * 12L;
        var scaleShearOffset = rotationOffset + rotationCount * 16L;
        var endOffset = scaleShearOffset + scaleShearCount * 36L;
        if (timelineOffset < 0 || endOffset > data.Length)
            return false;

        var translationTimes = ReadTimes(data, checked((int)translationTimeOffset), translationCount);
        var rotationTimes = ReadTimes(data, checked((int)rotationTimeOffset), rotationCount);
        var scaleShearTimes = ReadTimes(data, checked((int)scaleShearTimeOffset), scaleShearCount);
        if (translationTimes is null || rotationTimes is null || scaleShearTimes is null)
            return false;

        var translations = new Vector3[translationCount];
        for (var keyIndex = 0; keyIndex < translations.Length; keyIndex++)
        {
            var keyOffset = checked((int)translationOffset + keyIndex * 12);
            var value = new Vector3(
                ReadSingle(data, keyOffset),
                ReadSingle(data, keyOffset + 4),
                ReadSingle(data, keyOffset + 8)) * modelScale;
            if (!IsFinite(value))
                return false;
            translations[keyIndex] = value;
        }

        var rotations = new Quaternion[rotationCount];
        for (var keyIndex = 0; keyIndex < rotations.Length; keyIndex++)
        {
            var keyOffset = checked((int)rotationOffset + keyIndex * 16);
            var value = new Quaternion(
                ReadSingle(data, keyOffset),
                ReadSingle(data, keyOffset + 4),
                ReadSingle(data, keyOffset + 8),
                ReadSingle(data, keyOffset + 12));
            if (!IsFinite(value))
                return false;
            rotations[keyIndex] = value;
        }

        var scaleShears = new Matrix4x4[scaleShearCount];
        for (var keyIndex = 0; keyIndex < scaleShears.Length; keyIndex++)
        {
            var value = ReadScaleShear(data, checked((int)scaleShearOffset + keyIndex * 36));
            if (!IsFinite(value))
                return false;
            scaleShears[keyIndex] = value;
        }

        duration = MathF.Max(
            LastTime(translationTimes),
            MathF.Max(LastTime(rotationTimes), LastTime(scaleShearTimes)));
        track = new GrnTransformTrack(
            translationTimes,
            translations,
            rotationTimes,
            rotations,
            scaleShearTimes,
            scaleShears);
        return true;
    }

    private static bool TryReadInterleavedAnimationTrack(
        ReadOnlySpan<byte> data,
        int offset,
        int nextOffset,
        Vector3 modelScale,
        uint channelId,
        out GrnTransformTrack track,
        out float duration)
    {
        const int headerSize = 12;
        const int frameSize = 68;
        track = null!;
        duration = 0.0f;
        var end = nextOffset > offset && nextOffset <= data.Length ? nextOffset : data.Length;
        var payloadLength = end - offset - headerSize;
        if (payloadLength < frameSize || payloadLength % frameSize != 0)
            return false;

        var frameCount = payloadLength / frameSize;
        if (frameCount > MaximumAnimationKeysPerChannel)
            return false;

        var times = new float[frameCount];
        var translations = new Vector3[frameCount];
        var rotations = new Quaternion[frameCount];
        var scaleShears = new Matrix4x4[frameCount];
        var previousTime = float.MinValue;
        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            var frameOffset = offset + headerSize + frameIndex * frameSize;
            var time = ReadSingle(data, frameOffset);
            var translation = new Vector3(
                ReadSingle(data, frameOffset + 4),
                ReadSingle(data, frameOffset + 8),
                ReadSingle(data, frameOffset + 12)) * modelScale;
            var rotation = new Quaternion(
                ReadSingle(data, frameOffset + 16),
                ReadSingle(data, frameOffset + 20),
                ReadSingle(data, frameOffset + 24),
                ReadSingle(data, frameOffset + 28));
            var scaleShear = ReadScaleShear(data, frameOffset + 32);
            if (!float.IsFinite(time) || time < previousTime ||
                !IsFinite(translation) || !IsFinite(rotation) || !IsFinite(scaleShear))
                return false;

            times[frameIndex] = time;
            translations[frameIndex] = translation;
            rotations[frameIndex] = rotation;
            scaleShears[frameIndex] = scaleShear;
            previousTime = time;
        }

        duration = LastTime(times);
        track = new GrnTransformTrack(
            times,
            translations,
            (float[])times.Clone(),
            rotations,
            (float[])times.Clone(),
            scaleShears);
        return channelId != 0;
    }

    private static string ResolveTransformChannelName(
        uint channelId,
        int objectNameKey,
        IReadOnlyList<string> textEntries,
        IReadOnlyList<Dictionary<uint, uint>> objects,
        IReadOnlyList<uint> transformChannelObjectIds)
    {
        if (objectNameKey < 0 || channelId == 0 || channelId > transformChannelObjectIds.Count)
            return string.Empty;

        var objectId = transformChannelObjectIds[checked((int)channelId - 1)];
        if (objectId == 0 || objectId > objects.Count)
            return string.Empty;

        var dataExtension = objects[checked((int)objectId - 1)];
        if (!dataExtension.TryGetValue((uint)objectNameKey, out var textId) || textId >= textEntries.Count)
            return string.Empty;

        return textEntries[checked((int)textId)];
    }

    private static int ReadAnimationKeyCount(ReadOnlySpan<byte> data, int offset)
    {
        var value = ReadUInt32(data, offset);
        return value <= MaximumAnimationKeysPerChannel ? checked((int)value) : -1;
    }

    private static float[]? ReadTimes(ReadOnlySpan<byte> data, int offset, int count)
    {
        var result = new float[count];
        var previous = float.MinValue;
        for (var index = 0; index < result.Length; index++)
        {
            var value = ReadSingle(data, offset + index * 4);
            if (!float.IsFinite(value) || value < previous)
                return null;
            result[index] = value;
            previous = value;
        }

        return result;
    }

    private static float LastTime(IReadOnlyList<float> values) =>
        values.Count > 0 ? values[^1] : 0.0f;

    private static int CountTracks(GrnAnimationClip clip) =>
        clip.Tracks.Count(static track => track is not null);
}
