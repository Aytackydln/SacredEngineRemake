using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Granny.Native;

internal static class GrannyX64MeshReader
{
    private const int GrannyHeaderSize = 0x40;
    private const uint GrannyMainChunk = 0xCA5E0000;
    private const uint RenderingStatePresent = 0x3B;
    private const int MaximumElementCount = 1_000_000;

    public static GrannyDllMeshData Read(GrannyX64NativeApi api, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(payload);

        var slice = FindFirstSlice(payload);
        var temporaryDirectory = CreateTemporaryDirectory();
        var filePath = Path.Combine(temporaryDirectory, "model.grn");
        try
        {
            File.WriteAllBytes(filePath, slice);
            return ReadFile(api, filePath);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static GrannyDllMeshData ReadFile(GrannyX64NativeApi api, string filePath)
    {
        api.ThrowIfFailed(api.OpenModel(api.Handle, filePath, out var model), "open the Granny model");
        try
        {
            api.ThrowIfFailed(api.OpenSequence(model.Granny, model.Value, out var sequence), "open the Granny sequence");
            try
            {
                return ReadSequence(api, sequence);
            }
            finally
            {
                api.CloseSequence(sequence.Granny, sequence.Value);
            }
        }
        finally
        {
            api.CloseModel(model.Granny, model.Value);
        }
    }

    private static GrannyDllMeshData ReadSequence(GrannyX64NativeApi api, GrannyHandle sequence)
    {
        api.ThrowIfFailed(
            api.LockSequenceForRendering(sequence.Granny, sequence.Value, RenderingStatePresent, out var rendering),
            "lock the Granny sequence for rendering");
        try
        {
            api.ThrowIfFailed(
                api.GetRenderingStatesLeft(rendering.Granny, rendering.Value, out var stateCount),
                "read the Granny rendering-state count");
            if (stateCount > MaximumElementCount)
                throw new InvalidDataException($"The Granny model reported {stateCount} rendering states.");

            var buffers = new List<GrannyDllVertexBufferData>();
            var surfaces = new List<GrannyDllSurfaceData>();
            var bufferIndices = new Dictionary<NativeBufferKey, int>();
            var activeTransform = Matrix4x4.Identity;
            for (var stateIndex = 0; stateIndex < stateCount; stateIndex++)
                ReadState(api, rendering, buffers, surfaces, bufferIndices, ref activeTransform);
            return new GrannyDllMeshData(buffers.ToArray(), surfaces.ToArray());
        }
        finally
        {
            api.UnlockRendering(rendering.Granny, rendering.Value);
        }
    }

    private static void ReadState(
        GrannyX64NativeApi api,
        GrannyHandle rendering,
        List<GrannyDllVertexBufferData> buffers,
        List<GrannyDllSurfaceData> surfaces,
        Dictionary<NativeBufferKey, int> bufferIndices,
        ref Matrix4x4 activeTransform)
    {
        var state = Marshal.AllocHGlobal(GrannyX64RenderingStateLayout.BufferSize);
        try
        {
            Marshal.Copy(new byte[GrannyX64RenderingStateLayout.BufferSize], 0, state, GrannyX64RenderingStateLayout.BufferSize);
            api.ThrowIfFailed(api.LockNextRenderingState(rendering.Granny, rendering.Value, state), "read a Granny rendering state");
            try
            {
                var vertexCount = ReadCount(state, GrannyX64RenderingStateLayout.PositionCountOffset, "vertex");
                var textureCount = ReadCount(state, GrannyX64RenderingStateLayout.TextureCoordinateCountOffset, "texture-coordinate");
                var normalCount = ReadCount(state, GrannyX64RenderingStateLayout.NormalCountOffset, "normal");
                if (normalCount != vertexCount || textureCount != vertexCount)
                    throw new InvalidDataException(
                        $"The Granny rendering arrays disagree in length ({vertexCount}, {normalCount}, {textureCount}).");

                var positions = ReadPointer(state, GrannyX64RenderingStateLayout.PositionPointerOffset, "position");
                var textureCoordinates = ReadPointer(state, GrannyX64RenderingStateLayout.TextureCoordinatePointerOffset, "texture-coordinate");
                var normals = ReadPointer(state, GrannyX64RenderingStateLayout.NormalPointerOffset, "normal");
                var transform = ReadStateTransform(state, ref activeTransform);
                var key = new NativeBufferKey(positions, normals, textureCoordinates, vertexCount, transform);
                if (!bufferIndices.TryGetValue(key, out var bufferIndex))
                {
                    bufferIndex = buffers.Count;
                    bufferIndices.Add(key, bufferIndex);
                    var sourcePositions = ReadVector3Array(positions, vertexCount);
                    var sourceNormals = ReadVector3Array(normals, vertexCount);
                    buffers.Add(new GrannyDllVertexBufferData(
                        TransformPositions(sourcePositions, transform),
                        TransformNormals(sourceNormals, transform),
                        ReadTextureCoordinateArray(textureCoordinates, vertexCount)));
                }

                var triangleCount = ReadCount(state, GrannyX64RenderingStateLayout.TriangleCountOffset, "triangle");
                var indexCount = checked(triangleCount * 3);
                var indexPointer = ReadPointer(state, GrannyX64RenderingStateLayout.TrianglePointerOffset, "triangle-index");
                var signedIndices = new short[indexCount];
                Marshal.Copy(indexPointer, signedIndices, 0, signedIndices.Length);
                var indices = new ushort[indexCount];
                for (var index = 0; index < indices.Length; index++)
                {
                    indices[index] = unchecked((ushort)signedIndices[index]);
                    if (indices[index] >= vertexCount)
                        throw new InvalidDataException(
                            $"Granny returned vertex index {indices[index]} for a {vertexCount}-vertex buffer.");
                }

                surfaces.Add(new GrannyDllSurfaceData(bufferIndex, indices));
            }
            finally
            {
                api.UnlockRenderingState(state);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(state);
        }
    }

    private static byte[] FindFirstSlice(byte[] payload)
    {
        for (var mainOffset = GrannyHeaderSize; mainOffset + 8 <= payload.Length; mainOffset++)
        {
            if (BitConverter.ToUInt32(payload, mainOffset) != GrannyMainChunk ||
                !LooksLikeMainChunk(payload, mainOffset))
            {
                continue;
            }

            return payload[(mainOffset - GrannyHeaderSize)..];
        }

        throw new InvalidDataException("The model record does not contain a Granny 1 file.");
    }

    private static bool LooksLikeMainChunk(byte[] payload, int mainOffset)
    {
        var childCount = BitConverter.ToUInt32(payload, mainOffset + 4);
        if (childCount is 0 or > 16)
            return false;

        var descriptorOffset = mainOffset + 32;
        for (var child = 0; child < childCount; child++, descriptorOffset += 20)
        {
            if (descriptorOffset + 20 > payload.Length)
                return false;
            var chunk = BitConverter.ToUInt32(payload, descriptorOffset);
            if (chunk is not 0xCA5E0101 and not 0xCA5E0102 and not 0xCA5E0103)
                return false;
        }
        return true;
    }

    private static int ReadCount(nint state, int offset, string label)
    {
        var value = Marshal.ReadInt32(state, offset);
        if (value is <= 0 or > MaximumElementCount)
            throw new InvalidDataException($"Granny returned an invalid {label} count: {value}.");
        return value;
    }

    private static nint ReadPointer(nint state, int offset, string label)
    {
        var value = Marshal.ReadIntPtr(state, offset);
        if (value == 0)
            throw new InvalidDataException($"Granny did not return the requested {label} data.");
        return value;
    }

    private static Matrix4x4 ReadStateTransform(nint state, ref Matrix4x4 activeTransform)
    {
        if (Marshal.ReadInt32(state, GrannyX64RenderingStateLayout.LoadTransformOffset) == 0)
            return activeTransform;

        var pointer = ReadPointer(
            state,
            GrannyX64RenderingStateLayout.TransformPointerOffset,
            "state transform");
        var elements = new float[16];
        Marshal.Copy(pointer, elements, 0, elements.Length);
        var transform = new Matrix4x4(
            elements[0], elements[1], elements[2], elements[3],
            elements[4], elements[5], elements[6], elements[7],
            elements[8], elements[9], elements[10], elements[11],
            elements[12], elements[13], elements[14], elements[15]);
        if (!IsFinite(transform))
            throw new InvalidDataException("Granny returned a non-finite rendering-state transform.");

        activeTransform = transform;
        return activeTransform;
    }

    private static Vector3[] TransformPositions(IReadOnlyList<Vector3> source, Matrix4x4 transform)
    {
        var result = new Vector3[source.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var value = Vector3.Transform(source[index], transform);
            if (!IsFinite(value))
                throw new InvalidDataException("Granny returned a rendering-state transform with non-finite positions.");
            result[index] = value;
        }

        return result;
    }

    private static Vector3[] TransformNormals(IReadOnlyList<Vector3> source, Matrix4x4 transform)
    {
        if (!Matrix4x4.Invert(transform, out var inverse))
            throw new InvalidDataException("Granny returned a singular rendering-state transform.");

        var normalTransform = Matrix4x4.Transpose(inverse);
        var result = new Vector3[source.Count];
        for (var index = 0; index < result.Length; index++)
        {
            var value = Vector3.TransformNormal(source[index], normalTransform);
            if (!IsFinite(value))
                throw new InvalidDataException("Granny returned a rendering-state transform with non-finite normals.");
            result[index] = value.LengthSquared() > 0.000001f ? Vector3.Normalize(value) : Vector3.Zero;
        }

        return result;
    }

    private static Vector3[] ReadVector3Array(nint pointer, int count)
    {
        var components = new float[checked(count * 3)];
        Marshal.Copy(pointer, components, 0, components.Length);
        var result = new Vector3[count];
        for (var index = 0; index < result.Length; index++)
            result[index] = new Vector3(components[index * 3], components[index * 3 + 1], components[index * 3 + 2]);
        return result;
    }

    private static Vector2[] ReadTextureCoordinateArray(nint pointer, int count)
    {
        var components = new float[checked(count * 3)];
        Marshal.Copy(pointer, components, 0, components.Length);
        var result = new Vector2[count];
        for (var index = 0; index < result.Length; index++)
            result[index] = new Vector2(components[index * 3], components[index * 3 + 1]);
        return result;
    }

    private static bool IsFinite(Vector3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Matrix4x4 value) =>
        float.IsFinite(value.M11) && float.IsFinite(value.M12) && float.IsFinite(value.M13) && float.IsFinite(value.M14) &&
        float.IsFinite(value.M21) && float.IsFinite(value.M22) && float.IsFinite(value.M23) && float.IsFinite(value.M24) &&
        float.IsFinite(value.M31) && float.IsFinite(value.M32) && float.IsFinite(value.M33) && float.IsFinite(value.M34) &&
        float.IsFinite(value.M41) && float.IsFinite(value.M42) && float.IsFinite(value.M43) && float.IsFinite(value.M44);

    private static string CreateTemporaryDirectory()
    {
        var root = Path.GetFullPath(Path.GetTempPath());
        var path = Path.Combine(root, $"SacredGranny-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        if (!Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new IOException("The Granny temporary directory resolved outside the system temporary directory.");
        return path;
    }

    private readonly record struct NativeBufferKey(
        nint Positions,
        nint Normals,
        nint TextureCoordinates,
        int Count,
        Matrix4x4 Transform);
}

internal sealed record GrannyDllMeshData(GrannyDllVertexBufferData[] Buffers, GrannyDllSurfaceData[] Surfaces);

internal sealed record GrannyDllVertexBufferData(Vector3[] Positions, Vector3[] Normals, Vector2[] TextureCoordinates);

internal sealed record GrannyDllSurfaceData(int BufferIndex, ushort[] Indices);
