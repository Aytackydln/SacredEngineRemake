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

        var bounds = CalculateBounds(slice.Parts);
        var axis = VerticalAxis(bounds.Max - bounds.Min);
        var sideAxis = DetermineSideAxis(sourceSkeleton, axis);
        var center = Axis((bounds.Min + bounds.Max) * 0.5f, sideAxis);
        var partTieTransforms = new Matrix4x4?[slice.Parts.Length][];
        var partTargetBoneIndices = new int[slice.Parts.Length][];
        var mappedTieCount = 0;
        for (var partIndex = 0; partIndex < slice.Parts.Length; partIndex++)
        {
            var part = slice.Parts[partIndex];
            IReadOnlyList<uint> boneTieBones = part.BoneTieBones.Length > 0
                ? part.BoneTieBones
                : sourceSkeleton.BoneTieBones;
            var sideRemap = DetermineSingleSidedMirrorRemap(
                part,
                sourceSkeleton,
                boneTieBones,
                sideAxis,
                center);
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
                var targetBoneName = RemapSideBoneName(sourceBone.Name, sideRemap);
                var sourceRestBone = sourceBone;
                if (!string.Equals(targetBoneName, sourceBone.Name, StringComparison.Ordinal) &&
                    sourceSkeleton.BonesByName.TryGetValue(targetBoneName, out var remappedSourceBoneIndex))
                    sourceRestBone = sourceSkeleton.Bones[remappedSourceBoneIndex];

                if (string.IsNullOrWhiteSpace(targetBoneName) ||
                    !targetSkeleton.BonesByName.TryGetValue(targetBoneName, out var targetBoneIndex) ||
                    !Matrix4x4.Invert(sourceRestBone.RestWorld, out var inverseSourceRest))
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
            Parts = parts
        };
    }

    private static void AssociateNormalWithVertex(int[] mapping, uint normalIndex, uint vertexIndex)
    {
        if (normalIndex < mapping.Length && vertexIndex <= int.MaxValue && mapping[normalIndex] < 0)
            mapping[normalIndex] = checked((int)vertexIndex);
    }

    private static int DetermineSideAxis(GrannySkeleton skeleton, int verticalAxis)
    {
        var score = new float[3];
        for (var boneIndex = 0; boneIndex < skeleton.Bones.Length; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            if (!IsLeftSideBoneName(bone.Name))
                continue;

            var rightName = bone.Name.Replace(" L ", " R ", StringComparison.Ordinal);
            if (!skeleton.BonesByName.TryGetValue(rightName, out var rightBoneIndex))
                continue;

            var leftPosition = new Vector3(bone.RestWorld.M41, bone.RestWorld.M42, bone.RestWorld.M43);
            var rightBone = skeleton.Bones[rightBoneIndex];
            var rightPosition = new Vector3(rightBone.RestWorld.M41, rightBone.RestWorld.M42, rightBone.RestWorld.M43);
            var delta = Vector3.Abs(leftPosition - rightPosition);
            score[0] += delta.X;
            score[1] += delta.Y;
            score[2] += delta.Z;
        }

        score[verticalAxis] = -1.0f;
        var bestAxis = (verticalAxis + 1) % 3;
        for (var axis = 0; axis < score.Length; axis++)
        {
            if (score[axis] > score[bestAxis])
                bestAxis = axis;
        }

        return bestAxis;
    }

    private static SideRemap DetermineSingleSidedMirrorRemap(
        ParsedMeshPart part,
        GrannySkeleton sourceSkeleton,
        IReadOnlyList<uint> boneTieBones,
        int horizontalAxis,
        float center)
    {
        var usesLeft = false;
        var usesRight = false;
        var totalWeight = 0.0f;
        var sideWeight = 0.0f;
        var weightedBoneCenter = 0.0f;
        foreach (var vertexWeights in part.Weights)
        {
            foreach (var weight in vertexWeights)
            {
                if (weight.BoneTieIndex >= boneTieBones.Count)
                    continue;

                var boneIndex = boneTieBones[(int)weight.BoneTieIndex];
                if (boneIndex >= sourceSkeleton.Bones.Length)
                    continue;

                var bone = sourceSkeleton.Bones[boneIndex];
                var influence = float.IsFinite(weight.Weight) && weight.Weight > 0.0f
                    ? weight.Weight
                    : 1.0f;
                totalWeight += influence;

                var name = bone.Name;
                var left = IsLeftSideBoneName(name);
                var right = IsRightSideBoneName(name);
                usesLeft |= left;
                usesRight |= right;
                if (!left && !right)
                    continue;

                weightedBoneCenter += Axis(new Vector3(bone.RestWorld.M41, bone.RestWorld.M42, bone.RestWorld.M43), horizontalAxis) * influence;
                sideWeight += influence;
            }
        }

        if (usesLeft == usesRight)
            return SideRemap.None;
        if (sideWeight < totalWeight * 0.5f)
            return SideRemap.None;

        var partCenter = 0.0f;
        for (var i = 0; i < part.Positions.Length; i++)
            partCenter += Axis(part.Positions[i], horizontalAxis);
        partCenter /= Math.Max(1, part.Positions.Length);

        if (sideWeight <= 0.000001f)
            return SideRemap.None;

        weightedBoneCenter /= sideWeight;
        const float mirrorSideThreshold = 0.0001f;
        var partSide = partCenter - center;
        var boneSide = weightedBoneCenter - center;
        if (MathF.Abs(partSide) <= mirrorSideThreshold ||
            MathF.Abs(boneSide) <= mirrorSideThreshold ||
            partSide * boneSide >= 0.0f)
            return SideRemap.None;

        if (usesRight)
            return SideRemap.RightToLeft;
        if (usesLeft)
            return SideRemap.LeftToRight;

        return SideRemap.None;
    }

    private static string RemapSideBoneName(string name, SideRemap sideRemap) =>
        sideRemap switch
        {
            SideRemap.RightToLeft when IsRightSideBoneName(name) => name.Replace(" R ", " L ", StringComparison.Ordinal),
            SideRemap.LeftToRight when IsLeftSideBoneName(name) => name.Replace(" L ", " R ", StringComparison.Ordinal),
            _ => name
        };

    private static bool IsLeftSideBoneName(string name) =>
        name.Contains(" L ", StringComparison.Ordinal);

    private static bool IsRightSideBoneName(string name) =>
        name.Contains(" R ", StringComparison.Ordinal);
}

