using System.Numerics;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Native;

internal static class GrannyDllMeshBuilder
{
    private const int MaximumVertexCount = ushort.MaxValue + 1;

    public static Vector3 GetSourceOriginOffset(GrannyDllMeshData data, Vector3? modelScale)
    {
        var scale = SanitizeScale(modelScale ?? Vector3.One);
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        foreach (var buffer in data.Buffers)
        foreach (var position in buffer.Positions)
        {
            minimum = Vector3.Min(minimum, position * scale);
            maximum = Vector3.Max(maximum, position * scale);
        }
        var center = (minimum + maximum) * 0.5f;
        return new Vector3(center.X, center.Y, minimum.Z);
    }

    public static Mesh Build(GrannyDllMeshData data, Mesh? managedMesh, Vector3? modelScale)
    {
        if (data.Buffers.Length == 0 || data.Surfaces.Length == 0)
            throw new InvalidDataException("granny_x64.dll returned no renderable mesh data.");

        var scale = SanitizeScale(modelScale ?? Vector3.One);
        var bufferOffsets = new int[data.Buffers.Length];
        var vertexCount = 0;
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        for (var bufferIndex = 0; bufferIndex < data.Buffers.Length; bufferIndex++)
        {
            var buffer = data.Buffers[bufferIndex];
            ValidateBuffer(buffer);
            bufferOffsets[bufferIndex] = vertexCount;
            vertexCount = checked(vertexCount + buffer.Positions.Length);
            if (vertexCount > MaximumVertexCount)
                throw new NotSupportedException(
                    $"The granny_x64.dll mesh contains {vertexCount:N0} vertices; the renderer supports {MaximumVertexCount:N0}.");
            foreach (var sourcePosition in buffer.Positions)
            {
                var position = sourcePosition * scale;
                if (!IsFinite(position))
                    throw new InvalidDataException("granny_x64.dll returned a non-finite vertex position.");
                minimum = Vector3.Min(minimum, position);
                maximum = Vector3.Max(maximum, position);
            }
        }

        const int verticalAxis = GrnCoordinateSystem.VerticalAxis;
        const int horizontalAxis0 = GrnCoordinateSystem.HorizontalAxis0;
        const int horizontalAxis1 = GrnCoordinateSystem.HorizontalAxis1;
        var center = (minimum + maximum) * 0.5f;
        var inverseScale = new Vector3(1.0f / scale.X, 1.0f / scale.Y, 1.0f / scale.Z);
        var vertices = new VertexPositionNormalTexture[vertexCount];
        for (var bufferIndex = 0; bufferIndex < data.Buffers.Length; bufferIndex++)
        {
            var buffer = data.Buffers[bufferIndex];
            var targetOffset = bufferOffsets[bufferIndex];
            for (var sourceIndex = 0; sourceIndex < buffer.Positions.Length; sourceIndex++)
            {
                var sourcePosition = buffer.Positions[sourceIndex] * scale;
                var sourceNormal = NormalizeOrZero(buffer.Normals[sourceIndex] * inverseScale);
                vertices[targetOffset + sourceIndex] = new VertexPositionNormalTexture(
                    new Vector3(
                        Axis(sourcePosition, horizontalAxis0) - Axis(center, horizontalAxis0),
                        Axis(sourcePosition, horizontalAxis1) - Axis(center, horizontalAxis1),
                        Axis(sourcePosition, verticalAxis) - Axis(minimum, verticalAxis)),
                    NormalizeOrZero(new Vector3(
                        Axis(sourceNormal, horizontalAxis0),
                        Axis(sourceNormal, horizontalAxis1),
                        Axis(sourceNormal, verticalAxis))),
                    Sanitize(buffer.TextureCoordinates[sourceIndex]));
            }
        }

        var indices = new List<ushort>();
        var surfaces = new MeshSurface[data.Surfaces.Length];
        for (var surfaceIndex = 0; surfaceIndex < data.Surfaces.Length; surfaceIndex++)
        {
            var sourceSurface = data.Surfaces[surfaceIndex];
            if ((uint)sourceSurface.BufferIndex >= (uint)bufferOffsets.Length)
                throw new InvalidDataException($"granny_x64.dll referenced vertex buffer {sourceSurface.BufferIndex}.");

            var indexStart = indices.Count;
            var vertexOffset = bufferOffsets[sourceSurface.BufferIndex];
            var sourceVertexCount = data.Buffers[sourceSurface.BufferIndex].Positions.Length;
            foreach (var sourceIndex in sourceSurface.Indices)
            {
                if (sourceIndex >= sourceVertexCount)
                    throw new InvalidDataException($"granny_x64.dll returned vertex index {sourceIndex}.");
                indices.Add(checked((ushort)(vertexOffset + sourceIndex)));
            }

            surfaces[surfaceIndex] = new MeshSurface(
                indexStart,
                indices.Count - indexStart,
                null);
        }

        var indexArray = indices.ToArray();
        var mappedSurfaces = GrannyDllSurfaceTextureMapper.Map(
            vertices,
            indexArray,
            surfaces,
            managedMesh);
        return new Mesh(vertices, indexArray) { Surfaces = mappedSurfaces };
    }

    private static void ValidateBuffer(GrannyDllVertexBufferData buffer)
    {
        if (buffer.Positions.Length == 0 ||
            buffer.Normals.Length != buffer.Positions.Length ||
            buffer.TextureCoordinates.Length != buffer.Positions.Length)
        {
            throw new InvalidDataException(
                "granny_x64.dll returned position, normal, and texture-coordinate arrays with different lengths.");
        }
    }

    private static Vector3 SanitizeScale(Vector3 scale) =>
        IsFinite(scale) &&
        MathF.Abs(scale.X) > 0.000001f &&
        MathF.Abs(scale.Y) > 0.000001f &&
        MathF.Abs(scale.Z) > 0.000001f
            ? scale
            : Vector3.One;

    private static Vector2 Sanitize(Vector2 value) => new(
        float.IsFinite(value.X) ? value.X : 0.0f,
        float.IsFinite(value.Y) ? value.Y : 0.0f);

    private static float Axis(Vector3 value, int axis) => axis switch
    {
        0 => value.X,
        1 => value.Y,
        _ => value.Z
    };

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static Vector3 NormalizeOrZero(Vector3 value) =>
        IsFinite(value) && value.LengthSquared() > 0.000001f
            ? Vector3.Normalize(value)
            : Vector3.Zero;
}
