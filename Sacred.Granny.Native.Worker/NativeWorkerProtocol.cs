using System.Numerics;

namespace Sacred.Granny.Native.Worker;

internal static class NativeWorkerProtocol
{
    private const uint HandshakeMagic = 0x484E4753; // SGNH
    private const uint ResponseMagic = 0x574E4753; // SGNW
    private const int ResponseVersion = 1;
    private const int MaximumRequestBytes = 256 * 1024 * 1024;

    public static void WriteHandshake(BinaryWriter writer)
    {
        writer.Write(HandshakeMagic);
        writer.Write(ResponseVersion);
        writer.Flush();
    }

    public static bool TryReadRequest(BinaryReader reader, out byte[] payload)
    {
        payload = [];
        int length;
        try
        {
            length = reader.ReadInt32();
        }
        catch (EndOfStreamException)
        {
            return false;
        }

        if (length is <= 0 or > MaximumRequestBytes)
            throw new InvalidDataException($"Invalid Granny request length: {length}.");

        payload = reader.ReadBytes(length);
        if (payload.Length != length)
            throw new EndOfStreamException("The Granny request ended before its payload was complete.");
        return true;
    }

    public static byte[] WriteSuccess(NativeMeshData mesh) =>
        WriteResponse(writer =>
        {
            writer.Write(0);
            writer.Write(string.Empty);
            writer.Write(mesh.Buffers.Count);
            foreach (var buffer in mesh.Buffers)
            {
                writer.Write(buffer.Positions.Length);
                WriteVectors(writer, buffer.Positions);
                WriteVectors(writer, buffer.Normals);
                foreach (var coordinate in buffer.TextureCoordinates)
                {
                    writer.Write(coordinate.X);
                    writer.Write(coordinate.Y);
                }
            }

            writer.Write(mesh.Surfaces.Count);
            foreach (var surface in mesh.Surfaces)
            {
                writer.Write(surface.BufferIndex);
                writer.Write(surface.Indices.Length);
                foreach (var index in surface.Indices)
                    writer.Write(index);
            }
        });

    public static byte[] WriteFailure(string message) =>
        WriteResponse(writer =>
        {
            writer.Write(1);
            writer.Write(message);
        });

    private static byte[] WriteResponse(Action<BinaryWriter> writeBody)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(ResponseMagic);
        writer.Write(ResponseVersion);
        writeBody(writer);
        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteVectors(BinaryWriter writer, IReadOnlyList<Vector3> values)
    {
        foreach (var value in values)
        {
            writer.Write(value.X);
            writer.Write(value.Y);
            writer.Write(value.Z);
        }
    }
}

internal sealed record NativeMeshData(
    IReadOnlyList<NativeVertexBuffer> Buffers,
    IReadOnlyList<NativeSurface> Surfaces);

internal sealed record NativeVertexBuffer(
    Vector3[] Positions,
    Vector3[] Normals,
    Vector2[] TextureCoordinates);

internal sealed record NativeSurface(int BufferIndex, ushort[] Indices);
