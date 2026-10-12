using System.Buffers.Binary;

namespace Sacred.Network.TinCat;

/// <summary>Native Send_Data prepends two little-endian words before application data.</summary>
public static class TinCatCustomData
{
    public static byte[] Encode(ushort module, ushort type, ReadOnlySpan<byte> data)
    {
        var bytes = new byte[checked(data.Length + 4)];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, module);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), type);
        data.CopyTo(bytes.AsSpan(4));
        return bytes;
    }

    public static ReadOnlySpan<byte> Decode(ReadOnlySpan<byte> payload, ushort module, ushort type)
    {
        if (payload.Length < 4 || BinaryPrimitives.ReadUInt16LittleEndian(payload) != module ||
            BinaryPrimitives.ReadUInt16LittleEndian(payload[2..]) != type)
            throw new InvalidDataException("Unexpected TinCat application module or message type.");
        return payload[4..];
    }
}
