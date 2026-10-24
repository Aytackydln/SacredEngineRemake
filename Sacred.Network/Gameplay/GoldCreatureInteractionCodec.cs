using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldCreatureInteractionEvent(GoldMessage Message) : GoldCreatureCommandEvent(Message);
public sealed record GoldAlternatePickupEvent(GoldMessage Message, uint ItemReference, uint FacingBits)
    : GoldCreatureInteractionEvent(Message);
public sealed record GoldInventoryInsertEvent(GoldMessage Message, uint KeyWord, uint Word04)
    : GoldCreatureInteractionEvent(Message);
public sealed record GoldCreatureFxSpawnEvent(GoldMessage Message, uint ObjectReference, IReadOnlyList<uint> Operands)
    : GoldCreatureInteractionEvent(Message)
{
    public uint FxType => Message.Opcode switch { 0x14A => 0x314, 0x14D => 0x30D, _ => 0x30C };
    public uint NativeEventId => Message.Opcode switch { 0x14A => 0x14, 0x14D => 0xD, _ => 0xC };
    public bool RetiresSource => Message.Opcode == GoldCreatureInteractionCodec.RetiringFxOpcode;
}
public sealed record GoldCreatureDeathCommandEvent(GoldMessage Message, uint ObjectReference, uint PlayerWord)
    : GoldCreatureInteractionEvent(Message);
public sealed record GoldContainerOpenCommandEvent(GoldMessage Message, uint ObjectReference)
    : GoldCreatureInteractionEvent(Message);
public sealed record GoldRegionObjectTypesEvent(GoldMessage Message, int RegionIndex, int WordCount, uint GenerationWord,
    ReadOnlyMemory<byte> Record) : GoldCreatureInteractionEvent(Message)
{
    public int EntryCount => WordCount / 2;
}

/// <summary>Gold fixed interaction reads; 189 alone checks exact length before entering its helper.</summary>
public static class GoldCreatureInteractionCodec
{
    public const ushort AlternatePickupOpcode = 0x143;
    public const ushort InventoryInsertOpcode = 0x145;
    public const ushort PositionedFxOpcode = 0x14A;
    public const ushort AttachedFxOpcode = 0x14D;
    public const ushort RetiringFxOpcode = 0x14E;
    public const ushort DeathOpcode = 0x17D;
    public const ushort RegionTypesOpcode = 0x189;
    public const ushort ContainerOpenOpcode = 0x18B;
    public static GoldCreatureInteractionEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var size = message.Opcode switch
        {
            AlternatePickupOpcode or InventoryInsertOpcode or DeathOpcode => 8,
            PositionedFxOpcode or AttachedFxOpcode => 20, RetiringFxOpcode => 16,
            ContainerOpenOpcode => 4, RegionTypesOpcode => 184,
            _ => throw new InvalidDataException("Not a Gold creature interaction.")
        };
        if (message.Payload.Length < size || message.Opcode == RegionTypesOpcode && message.Payload.Length != size)
            throw new InvalidDataException("Invalid Gold creature interaction length.");
        var owned = message with { Payload = message.Payload.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(offset));
        return message.Opcode switch
        {
            AlternatePickupOpcode => new GoldAlternatePickupEvent(owned, Word(0), Word(4)),
            InventoryInsertOpcode => new GoldInventoryInsertEvent(owned, Word(0), Word(4)),
            PositionedFxOpcode or AttachedFxOpcode or RetiringFxOpcode => new GoldCreatureFxSpawnEvent(owned, Word(0),
                Array.AsReadOnly(Enumerable.Range(1, size / 4 - 1).Select(i => Word(i * 4)).ToArray())),
            DeathOpcode => new GoldCreatureDeathCommandEvent(owned, Word(0), Word(4)),
            ContainerOpenOpcode => new GoldContainerOpenCommandEvent(owned, Word(0)),
            _ => RegionRecord()
        };
        GoldRegionObjectTypesEvent RegionRecord()
        {
            var region = unchecked((int)Word(172)); var count = unchecked((int)Word(176));
            // Native has no upper bound and can overread/overwrite its 20-pair array. Reject that unsafe form.
            if (region is >= 0 and < 50 && count / 2 > 20)
                throw new InvalidDataException("Gold region type entries exceed the fixed record.");
            return new(owned, region, count, Word(180), owned.Payload.AsMemory(0, 172));
        }
    }
}
