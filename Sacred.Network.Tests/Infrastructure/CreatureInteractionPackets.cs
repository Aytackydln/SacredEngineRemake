using System.Buffers.Binary;
using Sacred.Core.Pax.Hero.Inventory;
using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;
using static Sacred.Network.Tests.Infrastructure.QuestDeltaPackets;

namespace Sacred.Network.Tests.Infrastructure;

internal static class CreatureInteractionPackets
{
    public static byte[] Buddy(uint id = 77, uint status = 2, int channel = 3, uint game = 1, string name = "User")
    {
        var bytes = new byte[0x220]; Put(bytes, 4, id); Put(bytes, 8, status); Put(bytes, 0x10C, game); Put(bytes, 0x210, unchecked((uint)channel));
        Text(12, name); Text(0x110, "Game"); Text(0x214, "Lobby"); return bytes;
        void Text(int offset, string value)
        {
            for (var i = 0; i < value.Length; i++) BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + i * 2), value[i]);
        }
    }
    public static byte[] Region(int region = 3, int count = 4, uint generation = 7)
    {
        var bytes = new byte[184]; Put(bytes, 0, 0x1234); Put(bytes, 4, 1); Put(bytes, 8, 999);
        for (var i = 0; i < 20; i++) { Put(bytes, 12 + i * 8, (uint)(i + 1)); Put(bytes, 16 + i * 8, (uint)(i + 100)); }
        Put(bytes, 172, unchecked((uint)region)); Put(bytes, 176, unchecked((uint)count)); Put(bytes, 180, generation); return bytes;
    }
    public static GoldMessage Inventory(params uint[] pairs)
    {
        var bytes = new byte[SacredSavedInventoryLayout.MinimumSize];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 2);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(SacredSavedInventoryLayout.QuestCountOffset), pairs.Length / 2);
        bytes = [..bytes.AsSpan(0, SacredSavedInventoryLayout.QuestRecordsOffset), ..Words(pairs), ..new byte[4]];
        return GoldInventoryCodec.Create(2, 2, SacredSavedInventory.FromPayload(bytes));
    }
    private static void Put(byte[] b, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(offset), value);
}
