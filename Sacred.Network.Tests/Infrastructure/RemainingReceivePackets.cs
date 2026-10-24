using System.Buffers.Binary;
using Sacred.Network.Gameplay;

namespace Sacred.Network.Tests.Infrastructure;

internal static class RemainingReceivePackets
{
    public static byte[] Words(params uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var i = 0; i < words.Length; i++) Put(bytes, i * 4, words[i]);
        return bytes;
    }
    public static void Put(byte[] bytes, int at, uint word) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), word);
    public static GoldGameEvent Event(ushort op, byte[] data, byte sender = 0) => GoldGameEventDecoder.Decode(new(op, sender, 1, 0, data));
    public static byte[] Controlled()
    {
        var state = new GoldObjectBaseState(500, 1, 0, 3, 100, 200, 1, 0, 0, 0, 2, 0).ToPayload();
        return [..Words(500, 1, 100, 200, 1, 3), ..state];
    }
    public static byte[] PartialFx(uint count = 0)
    {
        var body = new byte[GoldReceiveWorkCodec.PartialFxMinimum + count * 4];
        Put(body, 0, GoldObjectFxStateLayout.Marker); Put(body, 4, 0x20); Put(body, 182, count);
        for (var i = 0; i < count; i++) Put(body, 186 + i * 4, (uint)(500 + i));
        return [..Words(500, 1), ..body];
    }
    public static byte[] Batch(int count = 2)
    {
        var bytes = new byte[GoldFxBatchCodec.Size]; Put(bytes, 0, 2); Put(bytes, 4, 16);
        Put(bytes, 8 + 0x750, (uint)count); Put(bytes, 8 + 0x730, 3); bytes[8 + 0x754] = 2;
        for (var i = 0; i < 20; i++)
        {
            Put(bytes, 8 + i * 4, (uint)(70 + i));
            Put(bytes, 8 + 0x50 + i * 16, 3);
            Put(bytes, 8 + 0x50 + i * 16 + 4, (uint)(100 + i));
            Put(bytes, 8 + 0x50 + i * 16 + 8, (uint)(200 + i));
            Put(bytes, 8 + 0x50 + i * 16 + 12, 1);
            Put(bytes, 8 + 0x6E0 + i * 4, BitConverter.SingleToUInt32Bits(35.5f - i));
        }
        return bytes;
    }
    public static byte[] StartData() => [..new byte[560], ..LobbyLifecyclePackets.Raw(new byte[] { 3, 2, 1, 0, 8 })];
    public static byte[] CreatureTable()
    {
        var bytes = new byte[256 + 86]; Put(bytes, 0, 0x464943); Put(bytes, 4, 1); Put(bytes, 256, 42); bytes[260] = 2; return bytes;
    }
    public static byte[] Interpreter() => Words(4, 0x12345678, 0, 0, 0, 0, 0, 0);
    public static GoldObjectEvent FxObject()
    {
        var partial = PartialFx(2); var basis = new GoldObjectBaseState(500, 1, 0, 3, 100, 200, 1, 0, 0, 0, 2, 0).ToPayload();
        var body = new byte[basis.Length + partial.Length - 8]; basis.CopyTo(body, 0); partial.AsSpan(8).CopyTo(body.AsSpan(basis.Length));
        return (GoldObjectEvent)GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16F, 2, body));
    }
}
