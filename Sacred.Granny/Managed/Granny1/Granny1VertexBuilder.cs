using System.Numerics;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private static List<MeshSurface> BuildSequentialMesh(
        IReadOnlyList<ParsedMeshPart> parts,
        IReadOnlyList<TexturePolygon> texturePolygons,
        IReadOnlyList<TexturePolygonBlock> texturePolygonBlocks,
        IReadOnlyList<string> textureNames,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices,
        List<GrnSkinVertex>? skinVertices)
    {
        var surfaceRanges = BuildSurfaceRanges(texturePolygonBlocks, textureNames);
        var globalPolygonIndex = 0;

        foreach (var part in parts)
        {
            foreach (var polygon in part.Polygons)
            {
                if (vertices.Count + 3 > MaximumMeshVertices)
                    break;

                if (!IsValidPolygon(part, polygon))
                {
                    globalPolygonIndex++;
                    continue;
                }

                var texturePolygon = globalPolygonIndex < texturePolygons.Count
                    ? texturePolygons[globalPolygonIndex]
                    : default;

                AddCorner(part, polygon.A, polygon.NormalA, texturePolygon.B, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.B, polygon.NormalB, texturePolygon.C, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.C, polygon.NormalC, texturePolygon.D, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                globalPolygonIndex++;
            }
        }

        return surfaceRanges;
    }

    private static void AddCorner(
        ParsedMeshPart part,
        uint positionIndex,
        uint normalIndex,
        uint textureIndex,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        List<VertexPositionNormalTexture> vertices,
        List<ushort> indices,
        List<GrnSkinVertex>? skinVertices)
    {
        if (positionIndex >= part.Positions.Length)
            return;

        var source = part.Positions[positionIndex];
        var position = new Vector3(
            Axis(source, horizontalAxis0) - Axis(center, horizontalAxis0),
            Axis(source, horizontalAxis1) - Axis(center, horizontalAxis1),
            Axis(source, verticalAxis) - Axis(min, verticalAxis));

        var texCoord = textureIndex < part.TextureCoordinates.Length
            ? SanitizeTexCoord(part.TextureCoordinates[textureIndex])
            : Vector2.Zero;
        var sourceNormal = normalIndex < part.Normals.Length
            ? NormalizeOrZero(part.Normals[normalIndex])
            : Vector3.Zero;
        var normal = new Vector3(
            Axis(sourceNormal, horizontalAxis0),
            Axis(sourceNormal, horizontalAxis1),
            Axis(sourceNormal, verticalAxis));

        indices.Add(checked((ushort)vertices.Count));
        vertices.Add(new VertexPositionNormalTexture(position, NormalizeOrZero(normal), texCoord));
        skinVertices?.Add(CreateSkinVertex(part, checked((int)positionIndex), sourceNormal));
    }

    private static GrnSkinVertex CreateSkinVertex(
        ParsedMeshPart part,
        int positionIndex,
        Vector3 bindNormal)
    {
        if (part.RigidBoneIndex >= 0)
        {
            return new GrnSkinVertex(
                part.Positions[positionIndex],
                bindNormal,
                [new GrnBoneWeight(part.RigidBoneIndex, 1.0f)],
                UsesRigidBoneTransform: true);
        }

        if ((uint)positionIndex >= (uint)part.Weights.Length || part.TargetBoneIndices.Length == 0)
            return new GrnSkinVertex(part.Positions[positionIndex], bindNormal, []);

        var sourceWeights = part.Weights[positionIndex];
        if (sourceWeights.Length == 0)
            return new GrnSkinVertex(part.Positions[positionIndex], bindNormal, []);

        var combined = new Dictionary<int, float>();
        foreach (var sourceWeight in sourceWeights)
        {
            if (sourceWeight.BoneTieIndex >= part.TargetBoneIndices.Length ||
                !float.IsFinite(sourceWeight.Weight) || sourceWeight.Weight <= 0.0f)
                continue;

            var targetBoneIndex = part.TargetBoneIndices[sourceWeight.BoneTieIndex];
            if (targetBoneIndex < 0)
                continue;
            combined[targetBoneIndex] = combined.GetValueOrDefault(targetBoneIndex) + sourceWeight.Weight;
        }

        return new GrnSkinVertex(
            part.Positions[positionIndex],
            bindNormal,
            combined.Select(static pair => new GrnBoneWeight(pair.Key, pair.Value)).ToArray());
    }

    private static Vector2 SanitizeTexCoord(Vector2 texCoord) =>
        new(SanitizeTexCoordComponent(texCoord.X), SanitizeTexCoordComponent(texCoord.Y));

    private static float SanitizeTexCoordComponent(float value) =>
        float.IsFinite(value) ? value : 0.0f;

    private static bool IsValidPolygon(ParsedMeshPart part, GrannyPolygon polygon) =>
        polygon.A < part.Positions.Length &&
        polygon.B < part.Positions.Length &&
        polygon.C < part.Positions.Length;
}

