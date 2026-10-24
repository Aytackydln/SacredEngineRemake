using System.Buffers.Binary;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests.Infrastructure;

internal static class CreatureCommandPackets
{
    public static GoldGameEvent Actor(uint reference, string kind = "creature", uint flags = 0, byte[]? roster = null,
        uint oldDialog = 1, uint ki = 6, ushort optionBits = 31, bool tail = true)
    {
        var basis = new GoldObjectBaseState(reference, 1, flags, 1, 300, 400, 3, 0, 0, 0, 2, reference == 501 ? 901u : 900u);
        if (kind == "fx")
        {
            var fx = new byte[227]; basis.ToPayload().CopyTo(fx, 0); Put(fx, 41, GoldObjectFxStateLayout.Marker);
            return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16f, 1, fx));
        }
        var bytes = new byte[kind == "physical" ? 121 : 445]; basis.ToPayload().CopyTo(bytes, 0); Put(bytes, 41, GoldPhysicalStateLayout.Marker);
        if (kind == "creature")
        {
            Put(bytes, 121, GoldCreatureStateLayout.Marker); Put(bytes, GoldCreatureStateLayout.Word1F0Offset, ki);
            Put(bytes, GoldCreatureStateLayout.Word1F4Offset, 0xabcdefef); Put(bytes, GoldCreatureStateLayout.Word200Offset, 0xa000001f);
            Put(bytes, GoldCreatureStateLayout.Word245Offset, oldDialog); Put(bytes, GoldCreatureStateLayout.Word249Offset, 0x12345678);
            Put(bytes, GoldCreatureStateLayout.Word24DOffset, 0x22222222);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word2B7Offset), optionBits);
            if (tail)
            {
                var values = new byte[274]; Put(values, 0, GoldCreatureValuesLayout.Marker);
                roster ??= [];
                bytes = [..bytes, ..values, ..BitConverter.GetBytes((ushort)(roster.Length / 9)), ..roster, ..new byte[11]];
            }
        }
        return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16b, 1, bytes));
    }
    public static void Put(byte[] bytes, int offset, uint word) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), word);
    public static GoldMessage Admission() => new(0x11, 0, 2, 0, [0, 2, 0, 0, 0, 0]);
    public static GoldMessage Dialogs()
    {
        var rows = new byte[4][];
        for (var i = 0; i < rows.Length; i++) { rows[i] = new byte[80]; Put(rows[i], 76, (uint)(100 + i)); }
        return Bank(0x1b4, (0, rows));
    }
    public static byte[] Definition() => [..Words(0xdeadbeef, 500), ..Enumerable.Range(0, 348).Select(i => (byte)(i * 7))];
    public static byte[] Hit()
    {
        var words = Enumerable.Range(0, 18).Select(i => (uint)i).ToArray(); words[13] = uint.MaxValue; words[16] = 501; words[17] = 0x1234abcd;
        return [..Words(500), ..Words(words)];
    }
}
