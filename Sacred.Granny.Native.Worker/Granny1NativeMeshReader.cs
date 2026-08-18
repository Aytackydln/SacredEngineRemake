using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Granny.Native.Worker;

internal static class Granny1NativeMeshReader
{
    private const int GrannyHeaderSize = 0x40;
    private const uint GrannyMainChunk = 0xCA5E0000;
    private const int RenderingStateSize = 0xCC;
    private const uint RenderingStatePresent = 0x3B;
    private const int MaximumElementCount = 1_000_000;
    private const int PositionCountOffset = 0x38;
    private const int PositionPointerOffset = 0x40;
    private const int TextureCoordinateCountOffset = 0x80;
    private const int TextureCoordinatePointerOffset = 0x88;
    private const int NormalCountOffset = 0x98;
    private const int NormalPointerOffset = 0xA0;
    private const int TriangleCountOffset = 0xB0;
    private const int TrianglePointerOffset = 0xB8;

    public static NativeMeshData Read(Granny1NativeApi api, byte[] payload)
    {
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

    private static NativeMeshData ReadFile(Granny1NativeApi api, string filePath)
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

    private static NativeMeshData ReadSequence(Granny1NativeApi api, GrannyHandle sequence)
    {
        api.ThrowIfFailed(
            api.LockSequenceForRendering(
                sequence.Granny,
                sequence.Value,
                RenderingStatePresent,
                out var rendering),
            "lock the Granny sequence for rendering");
        try
        {
            api.ThrowIfFailed(
                api.GetRenderingStatesLeft(rendering.Granny, rendering.Value, out var stateCount),
                "read the Granny rendering-state count");
            if (stateCount > MaximumElementCount)
                throw new InvalidDataException($"The Granny model reported {stateCount} rendering states.");

            var buffers = new List<NativeVertexBuffer>();
            var surfaces = new List<NativeSurface>();
            var bufferIndices = new Dictionary<NativeBufferKey, int>();
            for (var stateIndex = 0; stateIndex < stateCount; stateIndex++)
                ReadState(api, rendering, buffers, surfaces, bufferIndices);
            return new NativeMeshData(buffers, surfaces);
        }
        finally
        {
            api.UnlockRendering(rendering.Granny, rendering.Value);
        }
    }

    private static void ReadState(
        Granny1NativeApi api,
        GrannyHandle rendering,
        List<NativeVertexBuffer> buffers,
        List<NativeSurface> surfaces,
        Dictionary<NativeBufferKey, int> bufferIndices)
    {
        var state = Marshal.AllocHGlobal(RenderingStateSize);
        try
        {
            Span<byte> empty = stackalloc byte[RenderingStateSize];
            Marshal.Copy(empty.ToArray(), 0, state, RenderingStateSize);
            api.ThrowIfFailed(
                api.LockNextRenderingState(rendering.Granny, rendering.Value, state),
                "read a Granny rendering state");
            try
            {
                var vertexCount = ReadCount(state, PositionCountOffset, "vertex");
                var textureCount = ReadCount(state, TextureCoordinateCountOffset, "texture-coordinate");
                var normalCount = ReadCount(state, NormalCountOffset, "normal");
                if (normalCount != vertexCount || textureCount != vertexCount)
                    throw new InvalidDataException(
                        $"The Granny rendering arrays disagree in length ({vertexCount}, {normalCount}, {textureCount}).");

                var positionPointer = ReadPointer(state, PositionPointerOffset, "position");
                var texturePointer = ReadPointer(state, TextureCoordinatePointerOffset, "texture-coordinate");
                var normalPointer = ReadPointer(state, NormalPointerOffset, "normal");
                var key = new NativeBufferKey(positionPointer, normalPointer, texturePointer, vertexCount);
                if (!bufferIndices.TryGetValue(key, out var bufferIndex))
                {
                    bufferIndex = buffers.Count;
                    bufferIndices.Add(key, bufferIndex);
                    buffers.Add(new NativeVertexBuffer(
                        ReadVector3Array(positionPointer, vertexCount),
                        ReadVector3Array(normalPointer, vertexCount),
                        ReadTextureCoordinateArray(texturePointer, vertexCount)));
                }

                var triangleCount = ReadCount(state, TriangleCountOffset, "triangle");
                var indexCount = checked(triangleCount * 3);
                var indexPointer = ReadPointer(state, TrianglePointerOffset, "triangle-index");
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

                surfaces.Add(new NativeSurface(bufferIndex, indices));
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
                continue;

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

    private static Vector3[] ReadVector3Array(nint pointer, int count)
    {
        var components = new float[checked(count * 3)];
        Marshal.Copy(pointer, components, 0, components.Length);
        var result = new Vector3[count];
        for (var index = 0; index < result.Length; index++)
            result[index] = new Vector3(
                components[index * 3],
                components[index * 3 + 1],
                components[index * 3 + 2]);
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
        int Count);
}
