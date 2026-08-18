using Sacred.Granny.Animation;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Managed.Granny1;

public static partial class Granny1MeshExtractor
{
    private static BuiltMesh BuildMesh(
        IReadOnlyList<ParsedMeshSlice> slices,
        IReadOnlyList<ParsedMeshPart>? projectionParts = null,
        GrannySkeleton? skinSkeleton = null)
    {
        var allParts = slices.SelectMany(static slice => slice.Parts).ToArray();
        var bounds = CalculateBounds(projectionParts ?? allParts);
        var axis = VerticalAxis(bounds.Max - bounds.Min);
        var horizontal0 = (axis + 1) % 3;
        var horizontal1 = (axis + 2) % 3;
        var center = (bounds.Min + bounds.Max) * 0.5f;

        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<ushort>();
        var surfaces = new List<MeshSurface>();
        var skinVertices = skinSkeleton is null ? null : new List<GrnSkinVertex>();

        foreach (var slice in slices)
        {
            var sliceIndexStart = indices.Count;
            var sliceSurfaces = BuildTexturedMeshFromBlocks(
                slice.Parts,
                slice.TexturePolygonBlocks,
                slice.TextureNames,
                bounds.Min,
                center,
                axis,
                horizontal0,
                horizontal1,
                vertices,
                indices,
                skinVertices);

            if (indices.Count == sliceIndexStart)
                sliceSurfaces = BuildSequentialMesh(
                    slice.Parts,
                    slice.TexturePolygons,
                    slice.TexturePolygonBlocks,
                    slice.TextureNames,
                    bounds.Min,
                    center,
                    axis,
                    horizontal0,
                    horizontal1,
                    vertices,
                    indices,
                    skinVertices);

            surfaces.AddRange(sliceSurfaces);
        }

        var vertexArray = vertices.ToArray();
        var indexArray = indices.ToArray();
        FillMissingNormals(vertexArray, indexArray);
        var mesh = new Mesh(vertexArray, indexArray)
        {
            Surfaces = ClipSurfaceRanges(surfaces, indexArray.Length)
        };
        if (skinSkeleton is null || skinVertices is null || skinVertices.Count != vertexArray.Length)
            return new BuiltMesh(mesh, null);

        var publicBones = skinSkeleton.Bones.Select(static bone => new GrnBone(
            bone.Name,
            bone.ParentIndex,
            bone.RestTranslation,
            bone.RestRotation,
            bone.RestScaleShear,
            bone.RestLocal,
            bone.RestWorld)).ToArray();
        var projection = new GrnMeshProjection(bounds.Min, center, axis, horizontal0, horizontal1);
        var finalSkinVertices = skinVertices.ToArray();
        for (var vertexIndex = 0; vertexIndex < finalSkinVertices.Length; vertexIndex++)
        {
            if (finalSkinVertices[vertexIndex].BindNormal.LengthSquared() <= 0.000001f)
            {
                finalSkinVertices[vertexIndex] = finalSkinVertices[vertexIndex] with
                {
                    BindNormal = NormalizeOrZero(projection.UnprojectDirection(vertexArray[vertexIndex].Normal))
                };
            }
        }

        var skin = new GrnMeshSkin(
            new GrnSkeleton(publicBones),
            finalSkinVertices,
            projection);
        return new BuiltMesh(mesh, skin);
    }

    private static void FillMissingNormals(
        VertexPositionNormalTexture[] vertices,
        ushort[] indices)
    {
        if (vertices.All(static vertex => vertex.Normal.LengthSquared() > 0.000001f))
            return;

        var generated = (VertexPositionNormalTexture[])vertices.Clone();
        GrnStaticMeshExtractor.RecalculateNormals(generated, indices);
        for (var vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
        {
            if (vertices[vertexIndex].Normal.LengthSquared() <= 0.000001f)
                vertices[vertexIndex] = vertices[vertexIndex] with { Normal = generated[vertexIndex].Normal };
        }
    }

    private static int CountPolygons(IEnumerable<ParsedMeshPart> parts) =>
        parts.Sum(static part => part.Polygons.Length);
}

