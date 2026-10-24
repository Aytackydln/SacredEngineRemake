using System.Buffers.Binary;
using System.Text;
using Sacred.Network.Gameplay;

namespace Sacred.Network.Tests.Infrastructure;

internal static class CounterReceivePackets
{
    public static byte[] Record(uint resource, uint remaining, uint initial = 10, string name = "Done")
    {
        var bytes = new byte[76];
        QuestDeltaPackets.Words(resource, remaining, initial).CopyTo(bytes, 0);
        Encoding.Latin1.GetBytes(name).AsSpan(0, Math.Min(name.Length, 64)).CopyTo(bytes.AsSpan(12)); return bytes;
    }
    public static byte[] Functions(params string[] names)
    {
        var bytes = new byte[4 + names.Length * 84]; BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)names.Length);
        for (var i = 0; i < names.Length; i++) Encoding.Latin1.GetBytes(names[i]).CopyTo(bytes, 4 + i * 84);
        return bytes;
    }
    public static GoldGameEvent Fx(uint flags = 0, int x = 100, int y = 200)
    {
        var bytes = new byte[GoldObjectFxStateLayout.MinimumSize];
        new GoldObjectBaseState(500, 42, flags, 1, x, y, 0, 0, 0, 0, 2, 0).ToPayload().CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(41), GoldObjectFxStateLayout.Marker);
        return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16f, 1, bytes));
    }
    public static byte[] Ball(uint clock = 0x3fc00000, byte flag = 1) =>
        [..QuestDeltaPackets.Words(500, 300, 400, 0xff, 0x80000000, 0x3f800000, 0x7fc00001, 0x41200000, 0x41a00000, 0x41f00000, clock), flag];
}
