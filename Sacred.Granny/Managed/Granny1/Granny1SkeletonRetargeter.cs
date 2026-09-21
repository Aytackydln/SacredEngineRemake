using System.Numerics;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private static ParsedMeshSlice? RetargetSlice(
        ParsedMeshSlice slice,
        GrannySkeleton? targetSkeleton)
    {
        var sourceSkeleton = slice.Skeleton;
        if (sourceSkeleton is null || targetSkeleton is null)
            return null;

        var partTieTransforms = new Matrix4x4?[slice.Parts.Length][];
        var partTargetBoneIndices = new int[slice.Parts.Length][];
        var mappedTieCount = 0;
        for (var partIndex = 0; partIndex < slice.Parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            IReadOnlyList<uint> boneTieBones = part.BoneTieBones.Length > 0
                ? part.BoneTieBones
                : sourceSkeleton.BoneTieBones;
            var tieTransforms = new Matrix4x4?[boneTieBones.Count];
            var targetBoneIndices = new int[boneTieBones.Count];
            Array.Fill(targetBoneIndices, -1);
            partTieTransforms[partIndex] = tieTransforms;
            partTargetBoneIndices[partIndex] = targetBoneIndices;

            for (var tieIndex = 0; tieIndex < boneTieBones.Count; tieIndex++)
            {
                var sourceBoneIndex = boneTieBones[tieIndex];
                if (sourceBoneIndex >= sourceSkeleton.Bones.Length)
                    continue;

                var sourceBone = sourceSkeleton.Bones[sourceBoneIndex];
                if (string.IsNullOrWhiteSpace(sourceBone.Name) ||
                    !targetSkeleton.BonesByName.TryGetValue(sourceBone.Name, out var targetBoneIndex) ||
                    !Matrix4x4.Invert(sourceBone.RestWorld, out var inverseSourceRest))
                    continue;

                // System.Numerics transforms row vectors, so the column-vector Granny skinning order is reversed.
                tieTransforms[tieIndex] = inverseSourceRest * targetSkeleton.Bones[targetBoneIndex].RestWorld;
                targetBoneIndices[tieIndex] = targetBoneIndex;
                mappedTieCount++;
            }
        }

        if (mappedTieCount == 0)
            return null;

        var parts = new ParsedMeshPart[slice.Parts.Length];
        for (var partIndex = 0; partIndex < parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            var positions = new Vector3[part.Positions.Length];
            for (var vertexIndex = 0; vertexIndex < positions.Length; vertexIndex++)
            {
                var sourcePosition = part.Positions[vertexIndex];
                var transformedPosition = Vector3.Zero;
                var totalWeight = 0.0f;
                foreach (var weight in part.Weights[vertexIndex])
                {
                    if (weight.BoneTieIndex >= partTieTransforms[partIndex].Length ||
                        partTieTransforms[partIndex][weight.BoneTieIndex] is not { } transform ||
                        !float.IsFinite(weight.Weight) ||
                        weight.Weight <= 0.0f)
                        continue;

                    transformedPosition += Vector3.Transform(sourcePosition, transform) * weight.Weight;
                    totalWeight += weight.Weight;
                }

                positions[vertexIndex] = totalWeight > 0.000001f
                    ? transformedPosition / totalWeight
                    : sourcePosition;
            }

            var normalSourceVertices = new int[part.Normals.Length];
            Array.Fill(normalSourceVertices, -1);
            foreach (var polygon in part.Polygons)
            {
                AssociateNormalWithVertex(normalSourceVertices, polygon.NormalA, polygon.A);
                AssociateNormalWithVertex(normalSourceVertices, polygon.NormalB, polygon.B);
                AssociateNormalWithVertex(normalSourceVertices, polygon.NormalC, polygon.C);
            }

            var normals = new Vector3[part.Normals.Length];
            for (var normalIndex = 0; normalIndex < normals.Length; normalIndex++)
            {
                var sourceNormal = part.Normals[normalIndex];
                var sourceVertexIndex = normalSourceVertices[normalIndex];
                if ((uint)sourceVertexIndex >= (uint)part.Weights.Length)
                {
                    normals[normalIndex] = sourceNormal;
                    continue;
                }

                var transformedNormal = Vector3.Zero;
                var totalWeight = 0.0f;
                foreach (var weight in part.Weights[sourceVertexIndex])
                {
                    if (weight.BoneTieIndex >= partTieTransforms[partIndex].Length ||
                        partTieTransforms[partIndex][weight.BoneTieIndex] is not { } transform ||
                        !float.IsFinite(weight.Weight) || weight.Weight <= 0.0f)
                        continue;

                    transformedNormal += Vector3.TransformNormal(sourceNormal, transform) * weight.Weight;
                    totalWeight += weight.Weight;
                }

                normals[normalIndex] = totalWeight > 0.000001f
                    ? NormalizeOrZero(transformedNormal / totalWeight)
                    : sourceNormal;
            }

            parts[partIndex] = part with
            {
                Positions = positions,
                Normals = normals,
                TargetBoneIndices = partTargetBoneIndices[partIndex]
            };
        }

        return slice with
        {
            Parts = parts,
            RetargetedAttachments = RetargetAttachments(sourceSkeleton, targetSkeleton)
        };
    }

    private static void AssociateNormalWithVertex(int[] mapping, uint normalIndex, uint vertexIndex)
    {
        if (normalIndex < mapping.Length && vertexIndex <= int.MaxValue && mapping[normalIndex] < 0)
            mapping[normalIndex] = checked((int)vertexIndex);
    }

}
