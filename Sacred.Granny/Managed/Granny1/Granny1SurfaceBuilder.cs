using System.Numerics;
using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private static List<MeshSurface> BuildTexturedMeshFromBlocks(
        IReadOnlyList<ParsedMeshPart> parts,
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
        var surfaces = new List<MeshSurface>(texturePolygonBlocks.Count);
        if (texturePolygonBlocks.Count == 0)
            return surfaces;

        var usedPolygons = parts.Select(static part => new HashSet<uint>()).ToArray();
        var blockPartAssignments = AssignTextureBlocksToParts(parts, texturePolygonBlocks);
        for (var blockIndex = 0; blockIndex < texturePolygonBlocks.Count; blockIndex++)
        {
            var block = texturePolygonBlocks[blockIndex];
            var partIndex = blockPartAssignments[blockIndex] >= 0
                ? blockPartAssignments[blockIndex]
                : BlockFitsPart(parts, block.FormSlot, block)
                    ? block.FormSlot
                    : SelectTextureBlockPart(parts, block, usedPolygons);
            if (partIndex < 0)
                continue;

            var part = parts[partIndex];
            var textureName = TextureNameForBlock(block, blockIndex, textureNames);
            var indexStart = indices.Count;
            foreach (var texturePolygon in block.Polygons)
            {
                if (vertices.Count + 3 > MaximumMeshVertices)
                    break;

                if (texturePolygon.A >= part.Polygons.Length ||
                    texturePolygon.B >= part.TextureCoordinates.Length ||
                    texturePolygon.C >= part.TextureCoordinates.Length ||
                    texturePolygon.D >= part.TextureCoordinates.Length)
                    continue;

                if (!usedPolygons[partIndex].Add(texturePolygon.A))
                    continue;

                var polygon = part.Polygons[checked((int)texturePolygon.A)];
                if (!IsValidPolygon(part, polygon))
                    continue;

                AddCorner(part, polygon.A, polygon.NormalA, texturePolygon.B, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.B, polygon.NormalB, texturePolygon.C, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.C, polygon.NormalC, texturePolygon.D, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
            }

            var indexCount = indices.Count - indexStart;
            if (indexCount > 0)
                surfaces.Add(new MeshSurface(
                    indexStart,
                    indexCount,
                    textureName));
        }

        var untexturedIndexStart = indices.Count;
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var used = usedPolygons[partIndex];
            for (var polygonIndex = 0; polygonIndex < part.Polygons.Length; polygonIndex++)
            {
                if (vertices.Count + 3 > MaximumMeshVertices)
                    break;

                if (used.Contains((uint)polygonIndex))
                    continue;

                var polygon = part.Polygons[polygonIndex];
                if (!IsValidPolygon(part, polygon))
                    continue;

                AddCorner(part, polygon.A, polygon.NormalA, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.B, polygon.NormalB, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
                AddCorner(part, polygon.C, polygon.NormalC, uint.MaxValue, min, center, verticalAxis, horizontalAxis0, horizontalAxis1, vertices, indices, skinVertices);
            }
        }

        var untexturedIndexCount = indices.Count - untexturedIndexStart;
        if (untexturedIndexCount > 0)
            surfaces.Add(new MeshSurface(untexturedIndexStart, untexturedIndexCount, null));

        return surfaces;
    }

    private static bool BlockFitsPart(
        IReadOnlyList<ParsedMeshPart> parts,
        int partIndex,
        TexturePolygonBlock block)
    {
        if (partIndex < 0 || partIndex >= parts.Count)
            return false;

        var part = parts[partIndex];
        foreach (var texturePolygon in block.Polygons)
        {
            if (texturePolygon.A >= part.Polygons.Length ||
                texturePolygon.B >= part.TextureCoordinates.Length ||
                texturePolygon.C >= part.TextureCoordinates.Length ||
                texturePolygon.D >= part.TextureCoordinates.Length)
                return false;
        }

        return true;
    }

    private static string? TextureNameForBlock(
        TexturePolygonBlock block,
        int blockIndex,
        IReadOnlyList<string> textureNames)
    {
        var textureIndex = block.TextureIndex > 0
            ? block.TextureIndex - 1
            : blockIndex;
        return textureIndex >= 0 && textureIndex < textureNames.Count
            ? textureNames[textureIndex]
            : null;
    }

    private static int[] AssignTextureBlocksToParts(
        IReadOnlyList<ParsedMeshPart> parts,
        IReadOnlyList<TexturePolygonBlock> texturePolygonBlocks)
    {
        var assignments = Enumerable.Repeat(-1, texturePolygonBlocks.Count).ToArray();
        var partsBySourceMesh = new Dictionary<int, int>();
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
            partsBySourceMesh.TryAdd(parts[partIndex].SourceMeshIndex, partIndex);

        for (var blockIndex = 0; blockIndex < texturePolygonBlocks.Count; blockIndex++)
        {
            var sourceMeshIndex = texturePolygonBlocks[blockIndex].SourceMeshIndex;
            if (sourceMeshIndex >= 0 &&
                partsBySourceMesh.TryGetValue(sourceMeshIndex, out var partIndex))
                assignments[blockIndex] = partIndex;
        }

        var usedParts = new bool[parts.Count];
        foreach (var partIndex in assignments)
        {
            if (partIndex >= 0)
                usedParts[partIndex] = true;
        }

        for (var groupStart = 0; groupStart < texturePolygonBlocks.Count;)
        {
            if (assignments[groupStart] >= 0)
            {
                groupStart++;
                continue;
            }

            var polygonCount = 0;
            var assignedPart = -1;
            var groupEnd = -1;
            for (var blockIndex = groupStart; blockIndex < texturePolygonBlocks.Count; blockIndex++)
            {
                if (assignments[blockIndex] >= 0)
                    break;

                polygonCount += texturePolygonBlocks[blockIndex].Polygons.Length;
                for (var partIndex = 0; partIndex < parts.Count; partIndex++)
                {
                    if (usedParts[partIndex] || parts[partIndex].Polygons.Length != polygonCount)
                        continue;

                    if (!TextureBlockGroupFitsPart(parts[partIndex], texturePolygonBlocks, groupStart, blockIndex))
                        continue;

                    assignedPart = partIndex;
                    groupEnd = blockIndex;
                    break;
                }

                if (assignedPart >= 0)
                    break;
            }

            if (assignedPart < 0)
            {
                groupStart++;
                continue;
            }

            usedParts[assignedPart] = true;
            for (var blockIndex = groupStart; blockIndex <= groupEnd; blockIndex++)
                assignments[blockIndex] = assignedPart;
            groupStart = groupEnd + 1;
        }

        return assignments;
    }

    private static bool TextureBlockGroupFitsPart(
        ParsedMeshPart part,
        IReadOnlyList<TexturePolygonBlock> texturePolygonBlocks,
        int groupStart,
        int groupEnd)
    {
        for (var blockIndex = groupStart; blockIndex <= groupEnd; blockIndex++)
        {
            foreach (var texturePolygon in texturePolygonBlocks[blockIndex].Polygons)
            {
                if (texturePolygon.A >= part.Polygons.Length ||
                    texturePolygon.B >= part.TextureCoordinates.Length ||
                    texturePolygon.C >= part.TextureCoordinates.Length ||
                    texturePolygon.D >= part.TextureCoordinates.Length)
                    return false;
            }
        }

        return true;
    }

    private static int SelectTextureBlockPart(
        IReadOnlyList<ParsedMeshPart> parts,
        TexturePolygonBlock block,
        IReadOnlyList<HashSet<uint>> usedPolygons)
    {
        var bestPartIndex = -1;
        var bestUnusedCount = -1;
        var bestValidCount = -1;
        for (var partIndex = 0; partIndex < parts.Count; partIndex++)
        {
            var part = parts[partIndex];
            var used = usedPolygons[partIndex];
            var validCount = 0;
            var unusedCount = 0;
            foreach (var texturePolygon in block.Polygons)
            {
                if (texturePolygon.A >= part.Polygons.Length ||
                    texturePolygon.B >= part.TextureCoordinates.Length ||
                    texturePolygon.C >= part.TextureCoordinates.Length ||
                    texturePolygon.D >= part.TextureCoordinates.Length)
                    continue;

                validCount++;
                if (!used.Contains(texturePolygon.A))
                    unusedCount++;
            }

            if (unusedCount < bestUnusedCount ||
                (unusedCount == bestUnusedCount && validCount <= bestValidCount))
                continue;

            bestPartIndex = partIndex;
            bestUnusedCount = unusedCount;
            bestValidCount = validCount;
        }

        return bestUnusedCount > 0 || bestValidCount > 0 ? bestPartIndex : -1;
    }
}

