using System.Buffers.Binary;
using System.Numerics;

namespace Sacred.Granny.Native;

internal static class GrannyDllWorkerProtocol
{
    private const uint HandshakeMagic = 0x484E4753;
    private const uint ResponseMagic = 0x574E4753;
    private const int ResponseVersion = 1;
    private const int StartupTimeoutMilliseconds = 5_000;
    private const int MaximumElementCount = 1_000_000;

    public static GrannyDllMeshData ReadResponse(byte[] response)
    {
        using var reader = new BinaryReader(new MemoryStream(response, writable: false));
        if (reader.ReadUInt32() != ResponseMagic || reader.ReadInt32() != ResponseVersion)
            throw new InvalidDataException("The Granny worker returned an incompatible response.");

        var status = reader.ReadInt32();
        var message = reader.ReadString();
        if (status != 0)
            throw new InvalidDataException(message);

        var bufferCount = ReadCount(reader, "vertex-buffer");
        var buffers = new GrannyDllVertexBufferData[bufferCount];
        for (var bufferIndex = 0; bufferIndex < buffers.Length; bufferIndex++)
        {
            var vertexCount = ReadCount(reader, "vertex");
            buffers[bufferIndex] = new GrannyDllVertexBufferData(
                ReadVector3Array(reader, vertexCount),
                ReadVector3Array(reader, vertexCount),
                ReadVector2Array(reader, vertexCount));
        }

        var surfaceCount = ReadCount(reader, "surface");
        var surfaces = new GrannyDllSurfaceData[surfaceCount];
        for (var surfaceIndex = 0; surfaceIndex < surfaces.Length; surfaceIndex++)
        {
            var bufferIndex = reader.ReadInt32();
            if ((uint)bufferIndex >= (uint)buffers.Length)
                throw new InvalidDataException($"The Granny worker referenced vertex buffer {bufferIndex}.");
            var indexCount = ReadCount(reader, "index");
            if (indexCount % 3 != 0)
                throw new InvalidDataException($"The Granny worker returned {indexCount} surface indices.");
            var indices = new ushort[indexCount];
            for (var index = 0; index < indices.Length; index++)
                indices[index] = reader.ReadUInt16();
            surfaces[surfaceIndex] = new GrannyDllSurfaceData(bufferIndex, indices);
        }

        if (reader.BaseStream.Position != reader.BaseStream.Length)
            throw new InvalidDataException("The Granny worker response contained unexpected trailing data.");
        return new GrannyDllMeshData(buffers, surfaces);
    }

    public static void ValidateHandshake(Stream output)
    {
        var handshake = new byte[8];
        using var timeout = new CancellationTokenSource(StartupTimeoutMilliseconds);
        output.ReadExactlyAsync(handshake, timeout.Token).AsTask().GetAwaiter().GetResult();
        if (BinaryPrimitives.ReadUInt32LittleEndian(handshake) != HandshakeMagic ||
            BinaryPrimitives.ReadInt32LittleEndian(handshake.AsSpan(4)) != ResponseVersion)
        {
            throw new InvalidDataException("The Granny worker uses an incompatible protocol.");
        }
    }

    private static Vector3[] ReadVector3Array(BinaryReader reader, int count)
    {
        var result = new Vector3[count];
        for (var index = 0; index < result.Length; index++)
            result[index] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return result;
    }

    private static Vector2[] ReadVector2Array(BinaryReader reader, int count)
    {
        var result = new Vector2[count];
        for (var index = 0; index < result.Length; index++)
            result[index] = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        return result;
    }

    private static int ReadCount(BinaryReader reader, string label)
    {
        var value = reader.ReadInt32();
        if (value is <= 0 or > MaximumElementCount)
            throw new InvalidDataException($"The Granny worker returned an invalid {label} count: {value}.");
        return value;
    }

}

internal sealed record GrannyDllMeshData(
    GrannyDllVertexBufferData[] Buffers,
    GrannyDllSurfaceData[] Surfaces);

internal sealed record GrannyDllVertexBufferData(
    Vector3[] Positions,
    Vector3[] Normals,
    Vector2[] TextureCoordinates);

internal sealed record GrannyDllSurfaceData(int BufferIndex, ushort[] Indices);

