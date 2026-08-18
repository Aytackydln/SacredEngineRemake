using System.Numerics;
using Sacred.Granny.Abstractions;
using Sacred.Granny.Animation;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private const int HeaderSize = 0x40;
    private const int ItemListHeaderSize = 0x10;
    private const uint MainChunk = 0xCA5E0000;
    private const uint FinalChunk = 0xCA5E0101;
    private const uint CopyrightChunk = 0xCA5E0102;
    private const uint ObjectChunk = 0xCA5E0103;
    private const uint TextChunk = 0xCA5E0200;
    private const uint TextureInfoChunk = 0xCA5E0301;
    private const uint TextureInfosChunk = 0xCA5E0304;
    private const uint BoneChunk = 0xCA5E0506;
    private const uint BoneListChunk = 0xCA5E0507;
    private const uint MeshChunk = 0xCA5E0601;
    private const uint MeshListChunk = 0xCA5E0602;
    private const uint PointChunk = 0xCA5E0801;
    private const uint NormalChunk = 0xCA5E0802;
    private const uint TexturePointChunk = 0xCA5E0803;
    private const uint WeightChunk = 0xCA5E0702;
    private const uint PolygonChunk = 0xCA5E0901;
    private const uint MeshIdChunk = 0xCA5E0F04;
    private const uint MaterialListChunk = 0xCA5E0D01;
    private const uint MaterialChunk = 0xCA5E0D00;
    private const uint MaterialTextureSlotChunk = 0xCA5E0D03;
    private const uint BoneObjectListChunk = 0xCA5E0B01;
    private const uint FormSectionChunk = 0xCA5E0C01;
    private const uint BoneObjectPointerChunk = 0xCA5E0C02;
    private const uint FormMeshChunk = 0xCA5E0C03;
    private const uint BoneObjectPointerEndChunk = 0xCA5E0C05;
    private const uint BoneTieChunk = 0xCA5E0C0A;
    private const uint TextureListChunk = 0xCA5E0E01;
    private const uint TexturePolygonGroupChunk = 0xCA5E0E02;
    private const uint TexturePolygonChunk = 0xCA5E0E06;
    private const uint ObjectListChunk = 0xCA5E0F03;
    private const uint ObjectDataChunk = 0xCA5E0F00;
    private const uint ObjectKeyChunk = 0xCA5E0F01;
    private const uint ObjectValueChunk = 0xCA5E0F02;
    private const int DescriptorSize = 12;
    private const int TextureCoordinateStride = 12;
    private const int MaximumMeshChildDescriptors = 128;
    private const int MaximumMeshVertices = ushort.MaxValue;

    public static Mesh? TryExtract(
        ReadOnlySpan<byte> data,
        GrnMeshExtractionMode extractionMode = GrnMeshExtractionMode.PrimarySlice)
    {
        return Extract(data, extractionMode).Mesh;
    }

    public static GrnExtractionResult Extract(
        ReadOnlySpan<byte> data,
        GrnMeshExtractionMode extractionMode = GrnMeshExtractionMode.PrimarySlice,
        Vector3? modelScale = null)
    {
        var slices = ExtractSlices(data);

        var scale = SanitizeModelScale(modelScale ?? Vector3.One);
        if (scale != Vector3.One)
            slices = slices.Select(slice => ApplyModelScale(slice, scale)).ToList();

        if (slices.Count == 0)
            return new GrnExtractionResult(null, new GrnModelDiagnostics([], null, null));

        IReadOnlyList<ParsedMeshSlice> renderedSlices = extractionMode == GrnMeshExtractionMode.CompositeSlices
            ? slices
            : [SelectPrimarySlice(slices)];
        var mesh = BuildMesh(renderedSlices).Mesh;
        return new GrnExtractionResult(mesh, CreateDiagnostics(slices, renderedSlices));
    }

    public static Mesh? TryExtractCharacter(
        ReadOnlySpan<byte> baseData,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        Vector3? baseModelScale = null) =>
        ExtractCharacter(baseData, attachments, baseModelScale).Mesh;

    public static GrnCharacterExtractionResult ExtractCharacter(
        ReadOnlySpan<byte> baseData,
        IReadOnlyList<GrnCharacterAttachment> attachments,
        Vector3? baseModelScale = null)
    {
        var baseSlices = ExtractSlices(baseData);
        if (baseSlices.Count == 0)
            return new GrnCharacterExtractionResult(
                null,
                null,
                new GrnModelDiagnostics([], null, null));

        var baseSlice = ApplyModelScale(
            SelectPrimarySlice(baseSlices),
            SanitizeModelScale(baseModelScale ?? Vector3.One));
        if (baseSlice.Skeleton is null)
            return new GrnCharacterExtractionResult(
                BuildMesh([baseSlice]).Mesh,
                null,
                CreateDiagnostics([baseSlice], [baseSlice]));

        baseSlice = BindSliceToSkeleton(baseSlice, baseSlice.Skeleton);

        var slices = new List<ParsedMeshSlice> { baseSlice };
        foreach (var attachmentSpec in attachments)
        {
            var attachmentSlices = ExtractSlices(attachmentSpec.Bytes);
            if (attachmentSlices.Count == 0)
                continue;

            var attachmentSlice = ApplyModelScale(
                SelectPrimarySlice(attachmentSlices),
                SanitizeModelScale(attachmentSpec.Scale));
            var attachment = string.IsNullOrWhiteSpace(attachmentSpec.RigidAttachBoneName)
                ? RetargetSlice(attachmentSlice, baseSlice.Skeleton)
                : AttachRigidSliceToBone(
                    attachmentSlice,
                    baseSlice.Skeleton,
                    attachmentSpec.RigidAttachBoneName,
                    attachmentSpec.SourceAttachBoneName);
            if (attachment is not null)
                slices.Add(attachment);
        }

        var result = BuildMesh(slices, baseSlice.Parts, baseSlice.Skeleton);
        return new GrnCharacterExtractionResult(
            result.Mesh,
            result.Skin,
            CreateDiagnostics(slices, [baseSlice]));
    }

    private static ParsedMeshSlice BindSliceToSkeleton(
        ParsedMeshSlice slice,
        GrannySkeleton targetSkeleton)
    {
        var parts = new ParsedMeshPart[slice.Parts.Length];
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            IReadOnlyList<uint> boneTieBones = part.BoneTieBones.Length > 0
                ? part.BoneTieBones
                : targetSkeleton.BoneTieBones;
            var targetBoneIndices = new int[boneTieBones.Count];
            Array.Fill(targetBoneIndices, -1);
            for (var tieIndex = 0; tieIndex < boneTieBones.Count; tieIndex++)
            {
                if (boneTieBones[tieIndex] < targetSkeleton.Bones.Length)
                    targetBoneIndices[tieIndex] = checked((int)boneTieBones[tieIndex]);
            }

            parts[partIndex] = part with { TargetBoneIndices = targetBoneIndices };
        }

        return slice with { Parts = parts };
    }

    private static ParsedMeshSlice ApplyModelScale(ParsedMeshSlice slice, Vector3 scale)
    {
        if (scale == Vector3.One)
            return slice;

        var parts = new ParsedMeshPart[slice.Parts.Length];
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            var positions = new Vector3[part.Positions.Length];
            for (var positionIndex = 0; positionIndex < positions.Length; positionIndex++)
                positions[positionIndex] = part.Positions[positionIndex] * scale;
            var normals = new Vector3[part.Normals.Length];
            var inverseScale = new Vector3(1.0f / scale.X, 1.0f / scale.Y, 1.0f / scale.Z);
            for (var normalIndex = 0; normalIndex < normals.Length; normalIndex++)
                normals[normalIndex] = NormalizeOrZero(part.Normals[normalIndex] * inverseScale);
            parts[partIndex] = part with { Positions = positions, Normals = normals };
        }

        return slice with
        {
            Parts = parts,
            Skeleton = slice.Skeleton is null ? null : ApplyModelScale(slice.Skeleton, scale)
        };
    }

    private static GrannySkeleton ApplyModelScale(GrannySkeleton skeleton, Vector3 scale)
    {
        var localTransforms = new Matrix4x4[skeleton.Bones.Length];
        var worldTransforms = new Matrix4x4[skeleton.Bones.Length];
        var parents = new int[skeleton.Bones.Length];
        for (var boneIndex = 0; boneIndex < skeleton.Bones.Length; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            parents[boneIndex] = bone.ParentIndex;
            localTransforms[boneIndex] = CreateBoneTransform(
                bone.RestRotation,
                bone.RestTranslation * scale,
                bone.RestScaleShear);
        }

        var states = new byte[skeleton.Bones.Length];
        for (var boneIndex = 0; boneIndex < skeleton.Bones.Length; boneIndex++)
        {
            if (!TryComputeBoneWorldTransform(boneIndex, parents, localTransforms, worldTransforms, states))
                return skeleton;
        }

        var bones = new GrannyBone[skeleton.Bones.Length];
        for (var boneIndex = 0; boneIndex < bones.Length; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            bones[boneIndex] = bone with
            {
                RestTranslation = bone.RestTranslation * scale,
                RestLocal = localTransforms[boneIndex],
                RestWorld = worldTransforms[boneIndex]
            };
        }

        return CreateSkeleton(bones, skeleton.BoneTieBones);
    }

    private static Vector3 SanitizeModelScale(Vector3 scale) =>
        IsFinite(scale) &&
        MathF.Abs(scale.X) > 0.000001f &&
        MathF.Abs(scale.Y) > 0.000001f &&
        MathF.Abs(scale.Z) > 0.000001f
            ? scale
            : Vector3.One;

    private static ParsedMeshSlice? AttachRigidSliceToBone(
        ParsedMeshSlice slice,
        GrannySkeleton? targetSkeleton,
        string targetBoneName,
        string? sourceBoneName)
    {
        if (targetSkeleton is null ||
            !targetSkeleton.BonesByName.TryGetValue(targetBoneName, out var targetBoneIndex))
            return null;

        if (!GrnRigidTransform.TryCreate(targetSkeleton.Bones[targetBoneIndex].RestWorld, out var transform))
            return null;

        if (!string.IsNullOrWhiteSpace(sourceBoneName))
        {
            if (slice.Skeleton is null ||
                !slice.Skeleton.BonesByName.TryGetValue(sourceBoneName, out var sourceBoneIndex) ||
                !GrnRigidTransform.TryCreate(
                    slice.Skeleton.Bones[sourceBoneIndex].RestWorld,
                    out var sourceBoneTransform) ||
                !Matrix4x4.Invert(sourceBoneTransform, out var inverseSourceBoneTransform))
            {
                return null;
            }

            transform = inverseSourceBoneTransform * transform;
        }

        var parts = new ParsedMeshPart[slice.Parts.Length];
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            var positions = new Vector3[part.Positions.Length];
            for (var vertexIndex = 0; vertexIndex < positions.Length; vertexIndex++)
                positions[vertexIndex] = Vector3.Transform(part.Positions[vertexIndex], transform);
            var normals = new Vector3[part.Normals.Length];
            for (var normalIndex = 0; normalIndex < normals.Length; normalIndex++)
                normals[normalIndex] = NormalizeOrZero(Vector3.TransformNormal(part.Normals[normalIndex], transform));

            parts[partIndex] = part with
            {
                Positions = positions,
                Normals = normals,
                RigidBoneIndex = targetBoneIndex
            };
        }

        return slice with
        {
            Parts = parts,
            Skeleton = slice.Skeleton is null
                ? null
                : TransformSkeletonWorld(slice.Skeleton, transform)
        };
    }

    private static GrannySkeleton TransformSkeletonWorld(GrannySkeleton skeleton, Matrix4x4 transform)
    {
        var bones = new GrannyBone[skeleton.Bones.Length];
        for (var index = 0; index < bones.Length; index++)
            bones[index] = skeleton.Bones[index] with
            {
                RestWorld = skeleton.Bones[index].RestWorld * transform
            };

        return CreateSkeleton(bones, skeleton.BoneTieBones);
    }
}
