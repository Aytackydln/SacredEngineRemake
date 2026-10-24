using System.Buffers.Binary;
using System.Text;
using Sacred.Network.Gameplay;

namespace Sacred.Network.Tests.Infrastructure;

internal static class ReceiveActionPackets
{
    public static GoldGameEvent Actor(int mode = 0, uint type = 1, uint flags = 0xa0000010)
    {
        var decoded = (GoldObjectEvent)QuestDeltaPackets.Actor(flags: mode == 2 ? 0x10u : 0u, physicalOnly: mode == 3);
        var bytes = decoded.ObjectState.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), type);
        if (mode != 3)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word200Offset), flags);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word24DOffset), 0x11111111);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.Word259Offset), 0x22222222);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.MovementPathIndexOffset), 5);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.MovementPathCountOffset), 7);
        }
        return GoldGameEventDecoder.Decode(GoldObjectStateCodec.Create(0x16b, 1, bytes));
    }
    public static byte[] Functions(params string[] names)
    {
        var bytes = new byte[4 + names.Length * 84]; BitConverter.GetBytes(names.Length).CopyTo(bytes, 0);
        for (var i = 0; i < names.Length; i++) Encoding.Latin1.GetBytes(names[i]).CopyTo(bytes, 4 + i * 84);
        return bytes;
    }
}
