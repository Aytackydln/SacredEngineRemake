using System.Buffers.Binary;
using System.IO.Compression;
using Sacred.Network.Admission;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests.Infrastructure;

internal static class LobbyLifecyclePackets
{
    public static byte[] Text(string value)
    {
        var result = new byte[(value.Length + 1) * 2];
        for (var i = 0; i < value.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(i * 2), value[i]);
        return result;
    }
    public static GoldMessage Roster(byte slot, string name)
    {
        var bytes = new byte[GoldPlayerRosterLayout.Size];
        bytes.AsSpan(slot * 834, 834).Fill(0xA5); bytes.AsSpan(slot * 834 + 0x16, 256).Clear();
        Text(name).CopyTo(bytes, slot * 834 + 0x16); return new(0x1B, 0, 2, 0, bytes);
    }
    public static GoldGameEvent Actor(uint reference, string kind = "creature", uint flags = 0, ushort sector = 1, byte owner = 2, uint context = 0)
    {
        var original = (GoldObjectEvent)CreatureCommandPackets.Actor(reference, kind, flags);
        var bytes = original.ObjectState.ToArray();
        var basis = original.BaseState with { Sector = sector, Owner = owner }; basis.ToPayload().CopyTo(bytes, 0);
        if (kind == "creature") BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.EquipmentOffset + 18 * 4), context);
        return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16B, 1, bytes));
    }
    public static byte[] Raw(byte[] data)
    {
        var result = new byte[4 + data.Length]; BinaryPrimitives.WriteUInt32LittleEndian(result, (uint)data.Length | 0x08000000);
        data.CopyTo(result, 4); uint previous = (uint)data.Length;
        for (var offset = 0; offset + 4 <= data.Length; offset += 4)
        {
            var encoded = BitConverter.ToUInt32(data, offset) ^ unchecked(previous + 0xB5D6C7A3);
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4 + offset), encoded); previous = encoded;
        }
        return result;
    }
    public static byte[] Compressed(byte[] data)
    {
        if (data.Length == 0) return [0, 0, 0, 0, 0x78, 0x9C, 3, 0, 0, 0, 0, 1];
        using var result = new MemoryStream(); result.Write(Words((uint)data.Length));
        using (var stream = new ZLibStream(result, CompressionLevel.Optimal, true)) stream.Write(data);
        return result.ToArray();
    }
    public static byte[] PublicProfile()
    {
        var bytes = new byte[0xD98]; BinaryPrimitives.WriteUInt16LittleEndian(bytes, 4);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(0x662), 123); return bytes;
    }
    public static byte[] PublicTransfer() => [..Words(123, 10, 0, 2), ..Compressed(PublicProfile())];
    public static byte[] PlayerTransfer() => [..Words(123, 2, 0, 0x230), ..Enumerable.Range(0, 556).Select(i => (byte)(i * 7))];
}
