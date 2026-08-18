using System.Numerics;
using Sacred.Granny.Assets;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private static GrnModelDiagnostics CreateDiagnostics(
        IReadOnlyList<ParsedMeshSlice> slices,
        IReadOnlyList<ParsedMeshSlice> renderedSlices)
    {
        var renderedParts = renderedSlices.SelectMany(static slice => slice.Parts).ToArray();
        var bounds = CalculateBounds(renderedParts);
        var verticalAxis = VerticalAxis(bounds.Max - bounds.Min);
        var horizontalAxis0 = (verticalAxis + 1) % 3;
        var horizontalAxis1 = (verticalAxis + 2) % 3;
        var center = (bounds.Min + bounds.Max) * 0.5f;
        const float scale = 1.0f;

        var projectedWholeModelPositions = slices
            .SelectMany(static slice => slice.Parts)
            .SelectMany(static part => part.Positions)
            .Select(position => ProjectPosition(
                position,
                bounds.Min,
                center,
                verticalAxis,
                horizontalAxis0,
                horizontalAxis1,
                scale))
            .ToArray();
        var wholeModelBounds = projectedWholeModelPositions.Length == 0
            ? (GrnBoundsDiagnostics?)null
            : new GrnBoundsDiagnostics(
                projectedWholeModelPositions.Aggregate(Vector3.Min),
                projectedWholeModelPositions.Aggregate(Vector3.Max));

        var sliceDiagnostics = slices.Select((slice, sliceIndex) =>
            new GrnSliceDiagnostics(
                sliceIndex,
                slice.Parts.Select((part, partIndex) => new GrnMeshPartDiagnostics(
                    partIndex,
                    part.Positions.Length,
                    part.Polygons.Length,
                    part.TextureCoordinates.Length,
                    part.Weights.Count(static weights => weights.Length > 0),
                    part.Weights.Sum(static weights => weights.Length))).ToArray(),
                slice.TextureNames,
                slice.TexturePolygons.Length,
                slice.TexturePolygonBlocks.Length,
                slice.Skeleton?.Bones.Select((bone, boneIndex) =>
                    new GrnBoneDiagnostics(
                        boneIndex,
                        bone.Name,
                        bone.ParentIndex,
                        ProjectPosition(
                            bone.RestWorld.Translation,
                            bounds.Min,
                            center,
                            verticalAxis,
                            horizontalAxis0,
                            horizontalAxis1,
                            scale))).ToArray() ?? [],
                slice.Skeleton?.BoneTieBones.Length ?? 0,
                CreateSurfaceTriangles(
                    slice.Parts,
                    bounds.Min,
                    center,
                    verticalAxis,
                    horizontalAxis0,
                    horizontalAxis1,
                    scale))).ToArray();
        var bonePositions = sliceDiagnostics
            .SelectMany(static slice => slice.Bones)
            .Select(static bone => bone.Position)
            .ToArray();
        var skeletonBounds = bonePositions.Length == 0
            ? (GrnBoundsDiagnostics?)null
            : new GrnBoundsDiagnostics(
                bonePositions.Aggregate(Vector3.Min),
                bonePositions.Aggregate(Vector3.Max));

        return new GrnModelDiagnostics(sliceDiagnostics, wholeModelBounds, skeletonBounds);
    }

    private static GrnSurfaceTriangleDiagnostics[] CreateSurfaceTriangles(
        IReadOnlyList<ParsedMeshPart> parts,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        float scale)
    {
        var triangles = new List<GrnSurfaceTriangleDiagnostics>();
        foreach (var part in parts)
        {
            foreach (var polygon in part.Polygons)
            {
                if (polygon.A >= part.Positions.Length ||
                    polygon.B >= part.Positions.Length ||
                    polygon.C >= part.Positions.Length)
                {
                    continue;
                }

                var a = ProjectPosition(part.Positions[polygon.A], min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale);
                var b = ProjectPosition(part.Positions[polygon.B], min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale);
                var c = ProjectPosition(part.Positions[polygon.C], min, center, verticalAxis, horizontalAxis0, horizontalAxis1, scale);
                var cross = Vector3.Cross(b - a, c - a);
                var doubleArea = cross.Length();
                if (!float.IsFinite(doubleArea) || doubleArea <= 0.000001f)
                    continue;

                triangles.Add(new GrnSurfaceTriangleDiagnostics(
                    a,
                    b,
                    c,
                    cross / doubleArea,
                    doubleArea * 0.5f));
            }
        }

        return triangles.ToArray();
    }

    private static Vector3 ProjectPosition(
        Vector3 source,
        Vector3 min,
        Vector3 center,
        int verticalAxis,
        int horizontalAxis0,
        int horizontalAxis1,
        float scale) =>
        new(
            (Axis(source, horizontalAxis0) - Axis(center, horizontalAxis0)) * scale,
            (Axis(source, horizontalAxis1) - Axis(center, horizontalAxis1)) * scale,
            (Axis(source, verticalAxis) - Axis(min, verticalAxis)) * scale);
}

