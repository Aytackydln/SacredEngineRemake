using System.Numerics;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private static GrannySkeleton? ReadSkeleton(
        ReadOnlySpan<byte> data,
        int descriptorScanLength,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var boneListIndex = FindImmediateChild(descriptors, -1, BoneListChunk);
        if (boneListIndex < 0)
            return null;

        var boneDescriptors = new List<ItemDescriptor>();
        var boneListEnd = DescendantEndExclusive(descriptors, boneListIndex);
        for (var descriptorIndex = boneListIndex + 1; descriptorIndex < boneListEnd; descriptorIndex++)
        {
            if (descriptors[descriptorIndex].Chunk == BoneChunk)
                boneDescriptors.Add(descriptors[descriptorIndex]);
        }

        if (boneDescriptors.Count == 0)
            return null;

        var textEntries = ReadTextEntries(data, descriptorScanLength, descriptors);
        var objects = ReadObjects(data, descriptors);
        var objectNameKey = FindStringIndex(textEntries, "__ObjectName");
        var boneObjectIds = ReadBoneObjectIds(data, descriptors);
        var boneObjectPointers = ReadBoneObjectPointers(data, descriptors);
        var boneTieBones = ReadBoneTieBones(data, descriptors);

        var localTransforms = new Matrix4x4[boneDescriptors.Count];
        var parents = new int[boneDescriptors.Count];
        var names = new string[boneDescriptors.Count];
        var translations = new Vector3[boneDescriptors.Count];
        var rotations = new Quaternion[boneDescriptors.Count];
        var scaleShears = new Matrix4x4[boneDescriptors.Count];
        for (var boneIndex = 0; boneIndex < boneDescriptors.Count; boneIndex++)
        {
            var dataOffset = boneDescriptors[boneIndex].DataOffset;
            if (dataOffset < 0 || dataOffset + 68 > data.Length)
                return null;

            var parentValue = ReadUInt32(data, dataOffset);
            parents[boneIndex] = parentValue < boneDescriptors.Count
                ? (int)parentValue
                : boneIndex;

            var translation = new Vector3(
                ReadSingle(data, dataOffset + 4),
                ReadSingle(data, dataOffset + 8),
                ReadSingle(data, dataOffset + 12));
            var rotation = new Quaternion(
                ReadSingle(data, dataOffset + 16),
                ReadSingle(data, dataOffset + 20),
                ReadSingle(data, dataOffset + 24),
                ReadSingle(data, dataOffset + 28));
            if (!IsFinite(translation) || !IsFinite(rotation))
                return null;

            var scaleShear = ReadScaleShear(data, dataOffset + 32);
            if (!IsFinite(scaleShear))
                return null;

            translations[boneIndex] = translation;
            rotations[boneIndex] = rotation.LengthSquared() > 0.000001f
                ? Quaternion.Normalize(rotation)
                : Quaternion.Identity;
            scaleShears[boneIndex] = scaleShear;
            localTransforms[boneIndex] = CreateBoneTransform(rotation, translation, scaleShear);
            names[boneIndex] = ResolveBoneName(
                boneIndex,
                objectNameKey,
                textEntries,
                objects,
                boneObjectIds,
                boneObjectPointers);
        }

        var worldTransforms = new Matrix4x4[boneDescriptors.Count];
        var transformStates = new byte[boneDescriptors.Count];
        for (var boneIndex = 0; boneIndex < boneDescriptors.Count; boneIndex++)
        {
            if (!TryComputeBoneWorldTransform(
                    boneIndex,
                    parents,
                    localTransforms,
                    worldTransforms,
                    transformStates))
                return null;
        }

        var bones = new GrannyBone[boneDescriptors.Count];
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            bones[boneIndex] = new GrannyBone(
                names[boneIndex],
                parents[boneIndex],
                translations[boneIndex],
                rotations[boneIndex],
                scaleShears[boneIndex],
                localTransforms[boneIndex],
                worldTransforms[boneIndex]);
        }

        return CreateSkeleton(bones, boneTieBones);
    }

    private static GrannySkeleton CreateSkeleton(GrannyBone[] bones, uint[] boneTieBones)
    {
        var bonesByName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            if (!string.IsNullOrWhiteSpace(bones[boneIndex].Name))
                bonesByName.TryAdd(bones[boneIndex].Name, boneIndex);
        }

        return new GrannySkeleton(bones, boneTieBones, bonesByName);
    }

    private static uint[] ReadBoneObjectIds(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var listIndex = FindImmediateChild(descriptors, -1, BoneObjectListChunk);
        if (listIndex < 0)
            return [];

        var objectIds = new List<uint>();
        var end = DescendantEndExclusive(descriptors, listIndex);
        for (var descriptorIndex = listIndex + 1; descriptorIndex < end; descriptorIndex++)
        {
            var descriptor = descriptors[descriptorIndex];
            if (descriptor.Chunk == MeshIdChunk &&
                descriptor.DataOffset >= 0 &&
                descriptor.DataOffset + 4 <= data.Length)
                objectIds.Add(ReadUInt32(data, descriptor.DataOffset));
        }

        return objectIds.ToArray();
    }

    private static uint[] ReadBoneObjectPointers(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var listIndex = FindImmediateChild(descriptors, -1, FormSectionChunk);
        if (listIndex < 0)
            return [];

        var startOffset = -1;
        var endOffset = -1;
        var end = DescendantEndExclusive(descriptors, listIndex);
        for (var descriptorIndex = listIndex + 1; descriptorIndex < end; descriptorIndex++)
        {
            var descriptor = descriptors[descriptorIndex];
            if (descriptor.Chunk == BoneObjectPointerChunk)
            {
                startOffset = descriptor.DataOffset;
            }
            else if (descriptor.Chunk == BoneObjectPointerEndChunk && startOffset >= 0)
            {
                endOffset = descriptor.DataOffset;
                break;
            }
        }

        if (startOffset < 0 ||
            endOffset <= startOffset ||
            endOffset > data.Length ||
            (endOffset - startOffset) % 4 != 0)
            return [];

        var pointers = new uint[(endOffset - startOffset) / 4];
        for (var pointerIndex = 0; pointerIndex < pointers.Length; pointerIndex++)
            pointers[pointerIndex] = ReadUInt32(data, startOffset + pointerIndex * 4);

        return pointers;
    }

    private static uint[] ReadBoneTieBones(
        ReadOnlySpan<byte> data,
        IReadOnlyList<ItemDescriptor> descriptors)
    {
        var listIndex = FindImmediateChild(descriptors, -1, FormSectionChunk);
        if (listIndex < 0)
            return [];

        var bones = new List<uint>();
        var end = DescendantEndExclusive(descriptors, listIndex);
        for (var descriptorIndex = listIndex + 1; descriptorIndex < end; descriptorIndex++)
        {
            var descriptor = descriptors[descriptorIndex];
            if (descriptor.Chunk == BoneTieChunk &&
                descriptor.DataOffset >= 0 &&
                descriptor.DataOffset + 4 <= data.Length)
                bones.Add(ReadUInt32(data, descriptor.DataOffset));
        }

        return bones.ToArray();
    }

    private static string ResolveBoneName(
        int boneIndex,
        int objectNameKey,
        IReadOnlyList<string> textEntries,
        IReadOnlyList<Dictionary<uint, uint>> objects,
        IReadOnlyList<uint> boneObjectIds,
        IReadOnlyList<uint> boneObjectPointers)
    {
        if (objectNameKey < 0 || boneIndex >= boneObjectPointers.Count)
            return string.Empty;

        var objectPointer = boneObjectPointers[boneIndex];
        if (objectPointer == 0 || objectPointer > boneObjectIds.Count)
            return string.Empty;

        var objectId = boneObjectIds[(int)objectPointer - 1];
        if (objectId == 0 || objectId > objects.Count)
            return string.Empty;

        var boneObject = objects[(int)objectId - 1];
        if (!boneObject.TryGetValue((uint)objectNameKey, out var textId) || textId >= textEntries.Count)
            return string.Empty;

        return textEntries[(int)textId];
    }

    private static Matrix4x4 CreateBoneTransform(Quaternion rotation, Vector3 translation, Matrix4x4 scaleShear)
    {
        var normalizedRotation = rotation.LengthSquared() > 0.000001f
            ? Quaternion.Normalize(rotation)
            : Quaternion.Identity;
        return scaleShear *
               Matrix4x4.CreateFromQuaternion(normalizedRotation) *
               Matrix4x4.CreateTranslation(translation);
    }

    private static Matrix4x4 ReadScaleShear(ReadOnlySpan<byte> data, int offset)
    {
        var m00 = ReadSingle(data, offset + 0);
        var m01 = ReadSingle(data, offset + 4);
        var m02 = ReadSingle(data, offset + 8);
        var m10 = ReadSingle(data, offset + 12);
        var m11 = ReadSingle(data, offset + 16);
        var m12 = ReadSingle(data, offset + 20);
        var m20 = ReadSingle(data, offset + 24);
        var m21 = ReadSingle(data, offset + 28);
        var m22 = ReadSingle(data, offset + 32);

        // Granny stores the 3x3 scale/shear block in the column-vector transform.
        // Matrix4x4/Vector3.Transform use row-vector order, so store the transpose.
        return new Matrix4x4(
            m00, m10, m20, 0.0f,
            m01, m11, m21, 0.0f,
            m02, m12, m22, 0.0f,
            0.0f, 0.0f, 0.0f, 1.0f);
    }

    private static bool TryComputeBoneWorldTransform(
        int boneIndex,
        IReadOnlyList<int> parents,
        IReadOnlyList<Matrix4x4> localTransforms,
        Matrix4x4[] worldTransforms,
        byte[] transformStates)
    {
        if (transformStates[boneIndex] == 2)
            return true;
        if (transformStates[boneIndex] == 1)
            return false;

        transformStates[boneIndex] = 1;
        var parentIndex = parents[boneIndex];
        if (parentIndex == boneIndex)
        {
            worldTransforms[boneIndex] = localTransforms[boneIndex];
        }
        else
        {
            if ((uint)parentIndex >= (uint)parents.Count ||
                !TryComputeBoneWorldTransform(
                    parentIndex,
                    parents,
                    localTransforms,
                    worldTransforms,
                    transformStates))
                return false;

            worldTransforms[boneIndex] = localTransforms[boneIndex] * worldTransforms[parentIndex];
        }

        transformStates[boneIndex] = 2;
        return true;
    }
}

