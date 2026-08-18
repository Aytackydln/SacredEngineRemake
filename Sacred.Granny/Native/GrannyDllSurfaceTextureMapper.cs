using Sacred.Granny.Meshes;

namespace Sacred.Granny.Native;

internal static class GrannyDllSurfaceTextureMapper
{
    private const double QuantizationScale = 10_000.0;
    private const int TextureCoordinateScore = 3;

    public static MeshSurface[] Map(
        IReadOnlyList<VertexPositionNormalTexture> nativeVertices,
        IReadOnlyList<ushort> nativeIndices,
        IReadOnlyList<MeshSurface> nativeSurfaces,
        Mesh? managedMesh)
    {
        if (managedMesh is null || managedMesh.Surfaces.Count == 0)
            return nativeSurfaces.ToArray();
        var positionLookup = BuildLookup(managedMesh, includeTextureCoordinates: false);
        var texturedLookup = BuildLookup(managedMesh, includeTextureCoordinates: true);
        var managedSurfaceIndices = new int[nativeSurfaces.Count];
        Array.Fill(managedSurfaceIndices, -1);
        for (var nativeSurfaceIndex = 0; nativeSurfaceIndex < nativeSurfaces.Count; nativeSurfaceIndex++)
        {
            var nativeSurface = nativeSurfaces[nativeSurfaceIndex];
            var scores = new int[managedMesh.Surfaces.Count];
            ScoreSurface(
                nativeVertices,
                nativeIndices,
                nativeSurface,
                positionLookup,
                scores,
                includeTextureCoordinates: false,
                score: 1);
            ScoreSurface(
                nativeVertices,
                nativeIndices,
                nativeSurface,
                texturedLookup,
                scores,
                includeTextureCoordinates: true,
                score: TextureCoordinateScore);
            managedSurfaceIndices[nativeSurfaceIndex] = SelectBestMatch(scores, nativeSurfaceIndex);
        }

        var result = new MeshSurface[nativeSurfaces.Count];
        for (var nativeSurfaceIndex = 0; nativeSurfaceIndex < nativeSurfaces.Count; nativeSurfaceIndex++)
        {
            var nativeSurface = nativeSurfaces[nativeSurfaceIndex];
            var managedSurfaceIndex = managedSurfaceIndices[nativeSurfaceIndex];
            if (managedSurfaceIndex < 0 && managedMesh.Surfaces.Count == 1)
                managedSurfaceIndex = 0;

            var textureName = managedSurfaceIndex >= 0
                ? managedMesh.Surfaces[managedSurfaceIndex].TextureName
                : null;
            result[nativeSurfaceIndex] = nativeSurface with { TextureName = textureName };
        }

        return result;
    }

    private static Dictionary<TriangleKey, List<int>> BuildLookup(
        Mesh mesh,
        bool includeTextureCoordinates)
    {
        var result = new Dictionary<TriangleKey, List<int>>();
        for (var surfaceIndex = 0; surfaceIndex < mesh.Surfaces.Count; surfaceIndex++)
        {
            var surface = mesh.Surfaces[surfaceIndex];
            VisitTriangles(
                mesh.Vertices,
                mesh.Indices,
                surface,
                includeTextureCoordinates,
                key => AddSurface(result, key, surfaceIndex));
        }
        return result;
    }

    private static void ScoreSurface(
        IReadOnlyList<VertexPositionNormalTexture> vertices,
        IReadOnlyList<ushort> indices,
        MeshSurface surface,
        IReadOnlyDictionary<TriangleKey, List<int>> lookup,
        int[] scores,
        bool includeTextureCoordinates,
        int score)
    {
        VisitTriangles(
            vertices,
            indices,
            surface,
            includeTextureCoordinates,
            key =>
            {
                if (!lookup.TryGetValue(key, out var managedSurfaceIndices))
                    return;
                foreach (var managedSurfaceIndex in managedSurfaceIndices)
                    scores[managedSurfaceIndex] += score;
            });
    }

    private static void VisitTriangles(
        IReadOnlyList<VertexPositionNormalTexture> vertices,
        IReadOnlyList<ushort> indices,
        MeshSurface surface,
        bool includeTextureCoordinates,
        Action<TriangleKey> visit)
    {
        var end = checked(surface.IndexStart + surface.IndexCount);
        for (var index = surface.IndexStart; index + 2 < end; index += 3)
        {
            var a = CreateVertexKey(vertices[indices[index]], includeTextureCoordinates);
            var b = CreateVertexKey(vertices[indices[index + 1]], includeTextureCoordinates);
            var c = CreateVertexKey(vertices[indices[index + 2]], includeTextureCoordinates);
            Sort(ref a, ref b, ref c);
            visit(new TriangleKey(a, b, c));
        }
    }

    private static void AddSurface(
        IDictionary<TriangleKey, List<int>> lookup,
        TriangleKey key,
        int surfaceIndex)
    {
        if (!lookup.TryGetValue(key, out var surfaceIndices))
        {
            surfaceIndices = [];
            lookup.Add(key, surfaceIndices);
        }
        if (!surfaceIndices.Contains(surfaceIndex))
            surfaceIndices.Add(surfaceIndex);
    }

    private static int SelectBestMatch(IReadOnlyList<int> scores, int preferredIndex)
    {
        var bestIndex = -1;
        var bestScore = 0;
        for (var index = 0; index < scores.Count; index++)
        {
            if (scores[index] > bestScore ||
                scores[index] == bestScore && bestScore > 0 && index == preferredIndex)
            {
                bestIndex = index;
                bestScore = scores[index];
            }
        }
        return bestIndex;
    }

    private static VertexKey CreateVertexKey(
        VertexPositionNormalTexture vertex,
        bool includeTextureCoordinates) =>
        new(
            Quantize(vertex.Position.X),
            Quantize(vertex.Position.Y),
            Quantize(vertex.Position.Z),
            includeTextureCoordinates ? Quantize(vertex.TexCoord.X) : 0,
            includeTextureCoordinates ? Quantize(vertex.TexCoord.Y) : 0);

    private static long Quantize(float value)
    {
        if (!float.IsFinite(value))
            return 0;

        var scaled = Math.Round(value * QuantizationScale, MidpointRounding.AwayFromZero);
        if (scaled >= long.MaxValue)
            return long.MaxValue;
        return scaled <= long.MinValue ? long.MinValue : (long)scaled;
    }

    private static void Sort(ref VertexKey a, ref VertexKey b, ref VertexKey c)
    {
        if (a.CompareTo(b) > 0)
            (a, b) = (b, a);
        if (b.CompareTo(c) > 0)
            (b, c) = (c, b);
        if (a.CompareTo(b) > 0)
            (a, b) = (b, a);
    }

    private readonly record struct TriangleKey(VertexKey A, VertexKey B, VertexKey C);

    private readonly record struct VertexKey(long X, long Y, long Z, long U, long V) : IComparable<VertexKey>
    {
        public int CompareTo(VertexKey other)
        {
            var comparison = X.CompareTo(other.X);
            if (comparison != 0)
                return comparison;
            comparison = Y.CompareTo(other.Y);
            if (comparison != 0)
                return comparison;
            comparison = Z.CompareTo(other.Z);
            if (comparison != 0)
                return comparison;
            comparison = U.CompareTo(other.U);
            return comparison != 0 ? comparison : V.CompareTo(other.V);
        }
    }
}
