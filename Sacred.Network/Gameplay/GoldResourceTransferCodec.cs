using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldCreatureTableEvent(GoldMessage Message, ReadOnlyMemory<byte> Header,
    IReadOnlyList<CreatureGoldRecordLayout> Records, ReadOnlyMemory<byte> RemainingData) : GoldReceiveWorkEvent(Message);
public sealed record GoldBannerEvent(GoldMessage Message, ReadOnlyMemory<byte> Image) : GoldReceiveWorkEvent(Message)
{
    public bool ClearsBanner => Image.IsEmpty;
}

/// <summary>Native whole-file transfers. Owned bytes are published in memory, independent of installation paths.</summary>
public static class GoldResourceTransferCodec
{
    public const ushort CreatureTableOpcode = 0x1AB;
    public const ushort BannerOpcode = 0x1AC;
    public static GoldReceiveWorkEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var owned = message with { Payload = message.Payload.ToArray() };
        if (message.Opcode == BannerOpcode) return new GoldBannerEvent(owned, owned.Payload);
        if (message.Opcode != CreatureTableOpcode || owned.Payload.Length < CreaturePakHeaderLayout.Size)
            throw new InvalidDataException("Truncated Gold Creature.pak transfer.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(4));
        if (count > (owned.Payload.Length - CreaturePakHeaderLayout.Size) / CreatureGoldRecordLayout.Size)
            throw new InvalidDataException("Truncated Gold Creature.pak table.");
        var records = new CreatureGoldRecordLayout[(int)count];
        for (var i = 0; i < records.Length; i++) records[i] = MemoryMarshal.Read<CreatureGoldRecordLayout>(
            owned.Payload.AsSpan(CreaturePakHeaderLayout.Size + i * CreatureGoldRecordLayout.Size));
        return new GoldCreatureTableEvent(owned, owned.Payload.AsMemory(0, CreaturePakHeaderLayout.Size), Array.AsReadOnly(records),
            owned.Payload.AsMemory(CreaturePakHeaderLayout.Size + records.Length * CreatureGoldRecordLayout.Size));
    }
}
