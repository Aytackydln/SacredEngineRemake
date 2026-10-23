using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Current-format saved creature, including value lists, two uint-counted lists and optional hero attachment.</summary>
public sealed class SacredSavedCreature
{
    private readonly byte[] _payload;
    private readonly int _records9Offset;
    private readonly int _records68Offset;
    private readonly int _attachmentOffset;
    private SacredSavedCreature(byte[] payload, SacredSavedCreatureValues values, int n, int m, int records9, int records68, int attachment)
        => (_payload, Values, Count9, Count68, _records9Offset, _records68Offset, _attachmentOffset) = (payload, values, n, m, records9, records68, attachment);
    public SacredSavedObjectBaseLayout BaseState => MemoryMarshal.Read<SacredSavedObjectBaseLayout>(_payload);
    public SacredSavedCreatureValues Values { get; }
    public int Count9 { get; }
    public int Count68 { get; }
    public int SerializedSize => _payload.Length;
    public uint Word200 => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.Word200Offset));
    public bool IsSleeping => (Word200 & SacredSavedCreatureLayout.SleepingFlag) != 0;
    public bool IsInFightMode => (Word200 & SacredSavedCreatureLayout.FightModeFlag) != 0;
    public int DialogIndex => BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.DialogIndexOffset));
    public SacredSavedCreatureThinkState Thinking => new(
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.ThinkIntervalOffset))),
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.ThinkElapsedOffset))),
        BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.KiModeOffset)),
        BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.EiModeOffset)));
    public uint MovementControl => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.MovementControlOffset));
    public uint MountPartnerReference => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.MountPartnerReferenceOffset));
    public ReadOnlySpan<byte> PhysicalData => _payload.AsSpan(0, SacredSavedPhysicalLayout.Size);
    public ReadOnlySpan<byte> CreaturePrefix => _payload.AsSpan(SacredSavedCreatureLayout.MarkerOffset, SacredSavedCreatureLayout.ValuesOffset - SacredSavedCreatureLayout.MarkerOffset);
    public ReadOnlySpan<byte> HeroAttachment => _payload.AsSpan(_attachmentOffset);
    public uint GetEquipmentReference(int index)
    {
        if ((uint)index >= SacredSavedCreatureLayout.EquipmentCount) throw new ArgumentOutOfRangeException(nameof(index));
        return BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureLayout.EquipmentOffset + index * 4));
    }
    public ReadOnlySpan<byte> GetRecord9(int index) => SacredSavedStreamBounds.Record(_payload, index, Count9, _records9Offset, 9);
    public ReadOnlySpan<byte> GetRecord68(int index) => SacredSavedStreamBounds.Record(_payload, index, Count68, _records68Offset, 68);
    public byte[] ToPayload() => _payload.ToArray();
    public static SacredSavedCreature FromPayload(ReadOnlySpan<byte> payload, byte version)
    {
        SacredSavedStreamBounds.RequireVersion(version);
        SacredSavedStreamBounds.Require(payload, SacredSavedCreatureLayout.ValuesOffset);
        var values = SacredSavedCreatureValues.FromPayload(payload[SacredSavedCreatureLayout.ValuesOffset..], version);
        var position = SacredSavedCreatureLayout.ValuesOffset + values.SerializedSize;
        var records9 = position + 4;
        var n = SacredSavedStreamBounds.List(payload, ref position, 4, 9);
        var records68 = position + 4;
        var m = SacredSavedStreamBounds.List(payload, ref position, 4, 68);
        var attachment = position;
        if ((BinaryPrimitives.ReadUInt32LittleEndian(payload[SacredSavedCreatureLayout.Word200Offset..]) & SacredSavedCreatureLayout.HeroAttachmentFlag) != 0)
            position = SacredSavedStreamBounds.Advance(payload, position, 1, SacredSavedCreatureLayout.HeroAttachmentSize);
        return new(payload[..position].ToArray(), values, n, m, records9, records68, attachment);
    }
}
