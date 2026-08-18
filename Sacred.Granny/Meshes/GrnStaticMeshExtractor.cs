using System.Numerics;

namespace Sacred.Granny.Meshes;

public static class GrnStaticMeshExtractor
{
    private const int MinimumIndexCount = 90;
    private const int MinimumUniqueIndices = 20;
    private const int MaximumVertexCount = 20000;
    private const int PositionStride = 12;
    private const int PositionSearchBackBytes = 64 * 1024;
    private const int MaximumMeshParts = 8;

    public static Mesh? TryExtract(ReadOnlySpan<byte> data)
    {
        var parts = new List<MeshPart>();
        foreach (var run in FindIndexRuns(data).OrderByDescending(static r => r.IndexCount))
        {
            if (parts.Count >= MaximumMeshParts)
                break;

            if (OverlapsExistingPart(parts, run.Start, run.ByteLength))
                continue;

            var positions = FindPositionBlock(data, run);
            if (positions is null)
                continue;

            parts.Add(new MeshPart(run, positions.Value));
        }

        if (parts.Count == 0)
            return null;

        return BuildMesh(parts);
    }

    private static List<IndexRun> FindIndexRuns(ReadOnlySpan<byte> data)
    {
        var runs = new List<IndexRun>();
        for (var offset = 0; offset + 4 <= data.Length;)
        {
            if ((offset & 3) != 0)
            {
                offset++;
                continue;
            }

            var start = offset;
            var values = new List<uint>();
            while (offset + 4 <= data.Length)
            {
                var value = BitConverter.ToUInt32(data.Slice(offset, 4));
                if (value >= MaximumVertexCount)
                    break;

                values.Add(value);
                offset += 4;
            }

            if (values.Count >= MinimumIndexCount)
            {
                var max = values.Max();
                var unique = values.Distinct().Count();
                if (max >= MinimumUniqueIndices && unique >= MinimumUniqueIndices && LooksLikeTriangleStream(values))
                    runs.Add(new IndexRun(start, values.ToArray(), checked((int)max + 1), unique));
            }

            offset = Math.Max(offset + 4, start + 4);
        }

        return runs;
    }

    private static bool LooksLikeTriangleStream(IReadOnlyList<uint> values)
    {
        var nonSequential = 0;
        for (var i = 1; i < values.Count; i++)
        {
            if (values[i] != values[i - 1] + 1)
                nonSequential++;
        }

        return nonSequential > values.Count * 0.6;
    }

    private static PositionBlock? FindPositionBlock(ReadOnlySpan<byte> data, IndexRun run)
    {
        var byteLength = run.VertexCount * PositionStride;
        if (byteLength <= 0 || run.Start < byteLength)
            return null;

        var bestScore = float.MinValue;
        PositionBlock? best = null;
        var searchStart = Math.Max(0, run.Start - byteLength - PositionSearchBackBytes);
        var searchEnd = run.Start - byteLength;

        for (var start = searchEnd; start >= searchStart; start -= 4)
        {
            if (!TryReadPositionBlock(data, start, run.VertexCount, out var block))
                continue;

            var score = block.Score + run.IndexCount * 0.01f;
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = block;
        }

        return best;
    }

    private static bool TryReadPositionBlock(ReadOnlySpan<byte> data, int start, int vertexCount, out PositionBlock block)
    {
        block = default;
        if (start < 0 || start + vertexCount * PositionStride > data.Length)
            return false;

        var positions = new Vector3[vertexCount];
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        var nonZero = 0;

        for (var i = 0; i < vertexCount; i++)
        {
            var offset = start + i * PositionStride;
            var position = new Vector3(
                BitConverter.ToSingle(data.Slice(offset + 0, 4)),
                BitConverter.ToSingle(data.Slice(offset + 4, 4)),
                BitConverter.ToSingle(data.Slice(offset + 8, 4)));

            if (!IsPlausiblePosition(position))
                return false;

            if (MathF.Abs(position.X) > 0.1f || MathF.Abs(position.Y) > 0.1f || MathF.Abs(position.Z) > 0.1f)
                nonZero++;

            positions[i] = position;
            min = Vector3.Min(min, position);
            max = Vector3.Max(max, position);
        }

        if (nonZero < vertexCount * 0.70f)
            return false;

        var span = max - min;
        var wideAxes = 0;
        if (span.X > 5.0f) wideAxes++;
        if (span.Y > 5.0f) wideAxes++;
        if (span.Z > 5.0f) wideAxes++;

        if (span.Length() < 20.0f || span.Length() > 600.0f || wideAxes < 2)
            return false;

        block = new PositionBlock(start, positions, min, max, span.X + span.Y + span.Z);
        return true;
    }

    private static bool IsPlausiblePosition(Vector3 position) =>
        float.IsFinite(position.X) &&
        float.IsFinite(position.Y) &&
        float.IsFinite(position.Z) &&
        position.X is > -2000.0f and < 2000.0f &&
        position.Y is > -2000.0f and < 2000.0f &&
        position.Z is > -2000.0f and < 2000.0f;

    private static Mesh BuildMesh(IReadOnlyList<MeshPart> parts)
    {
        var rawPositions = new List<Vector3>();
        foreach (var part in parts)
            rawPositions.AddRange(part.Positions.Positions);

        var globalMin = new Vector3(float.MaxValue);
        var globalMax = new Vector3(float.MinValue);
        foreach (var position in rawPositions)
        {
            globalMin = Vector3.Min(globalMin, position);
            globalMax = Vector3.Max(globalMax, position);
        }

        var span = globalMax - globalMin;
        var verticalAxis = span.X >= span.Y && span.X >= span.Z
            ? 0
            : span.Y >= span.Z ? 1 : 2;
        const float scale = 1.0f;

        var rawCenter = (globalMin + globalMax) * 0.5f;
        var bottom = Axis(globalMin, verticalAxis);
        var vertices = new List<VertexPositionNormalTexture>();
        var indices = new List<ushort>();

        foreach (var part in parts)
        {
            if (vertices.Count + part.Positions.Positions.Length > ushort.MaxValue)
                break;

            var vertexBase = vertices.Count;
            foreach (var position in part.Positions.Positions)
                vertices.Add(new VertexPositionNormalTexture(ProjectPosition(position, rawCenter, bottom, verticalAxis, scale), Vector3.Zero, Vector2.Zero));

            var usableIndexCount = part.Indices.Values.Length - part.Indices.Values.Length % 3;
            for (var i = 0; i < usableIndexCount; i++)
            {
                var sourceIndex = part.Indices.Values[i];
                if (sourceIndex >= part.Positions.Positions.Length)
                    continue;

                indices.Add(checked((ushort)(vertexBase + (int)sourceIndex)));
            }
        }

        var vertexArray = vertices.ToArray();
        var indexArray = indices.ToArray();
        RecalculateNormals(vertexArray, indexArray);
        return new Mesh(vertexArray, indexArray);
    }

    private static Vector3 ProjectPosition(Vector3 source, Vector3 center, float bottom, int verticalAxis, float scale)
    {
        var a = Axis(source, verticalAxis);
        var h0 = (verticalAxis + 1) % 3;
        var h1 = (verticalAxis + 2) % 3;

        return new Vector3(
            (Axis(source, h0) - Axis(center, h0)) * scale,
            (Axis(source, h1) - Axis(center, h1)) * scale,
            (a - bottom) * scale);
    }

    private static float Axis(Vector3 value, int axis) =>
        axis switch
        {
            0 => value.X,
            1 => value.Y,
            _ => value.Z
        };

    internal static void RecalculateNormals(VertexPositionNormalTexture[] vertices, ushort[] indices)
    {
        var normals = new Vector3[vertices.Length];
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            var i0 = indices[i + 0];
            var i1 = indices[i + 1];
            var i2 = indices[i + 2];
            var p0 = vertices[i0].Position;
            var p1 = vertices[i1].Position;
            var p2 = vertices[i2].Position;
            var normal = Vector3.Cross(p1 - p0, p2 - p0);
            if (normal.LengthSquared() <= 0.000001f)
                continue;

            normals[i0] += normal;
            normals[i1] += normal;
            normals[i2] += normal;
        }

        for (var i = 0; i < vertices.Length; i++)
        {
            var normal = normals[i].LengthSquared() > 0.000001f
                ? Vector3.Normalize(normals[i])
                : Vector3.UnitZ;
            vertices[i] = vertices[i] with { Normal = normal };
        }
    }

    private static bool OverlapsExistingPart(IEnumerable<MeshPart> parts, int start, int length)
    {
        var end = start + length;
        foreach (var part in parts)
        {
            if (start < part.Indices.Start + part.Indices.ByteLength && end > part.Indices.Start)
                return true;

            if (start < part.Positions.Start + part.Positions.ByteLength && end > part.Positions.Start)
                return true;
        }

        return false;
    }

    private readonly record struct IndexRun(int Start, uint[] Values, int VertexCount, int UniqueCount)
    {
        public int IndexCount => Values.Length;
        public int ByteLength => Values.Length * 4;
    }

    private readonly record struct PositionBlock(int Start, Vector3[] Positions, Vector3 Min, Vector3 Max, float Score)
    {
        public int ByteLength => Positions.Length * PositionStride;
    }

    private readonly record struct MeshPart(IndexRun Indices, PositionBlock Positions);
}
