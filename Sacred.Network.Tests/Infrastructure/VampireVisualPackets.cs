using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Tests.Infrastructure;

internal static class VampireVisualPackets
{
    public static byte[] SpecialPayload(ushort code)
    {
        var bytes = new byte[92];
        SeraphimVisualPackets.Word(bytes, 0, 500);
        SeraphimVisualPackets.Word(bytes, 4, 84);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(44), code);
        return bytes;
    }
    public static GoldMessage Special(ushort code) => new(0x12D, 0, 1, 21, SpecialPayload(code));
}
