using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless embedded values, with bounded 22/38-byte lists and native-offset field names.</summary>
public sealed class GoldCreatureValuesState
{
    private readonly byte[] _payload;
    private readonly GoldCreatureValuesBounds _bounds;

    private GoldCreatureValuesState(byte[] payload, GoldCreatureValuesBounds bounds, bool full)
    {
        _payload = payload;
        _bounds = bounds;
        IsFull = full;
    }

    public bool IsFull { get; }
    public int SerializedSize => _bounds.End;
    public ReadOnlySpan<byte> Block128 => _payload.AsSpan(GoldCreatureValuesLayout.Block128Offset, GoldCreatureValuesLayout.Block128Size);
    public ReadOnlySpan<byte> FullBlock08 => IsFull
        ? _payload.AsSpan(GoldCreatureValuesLayout.FullBlock08Offset, GoldCreatureValuesLayout.FullBlock08Size) : [];
    public uint Word0C => Word(FieldOffset(0x0C, GoldCreatureValuesLayout.CompactWord0COffset));
    public uint Experience => Word0C;
    public uint Gold => Word46;
    public ushort Level => Word56;
    /// <summary>Native calculated +0x3C. Compact packets omit it; callers must derive it from gameplay inputs.</summary>
    public short? AttackSpeed => IsFull ? BinaryPrimitives.ReadInt16LittleEndian(FullBlock08[52..]) : null;
    /// <summary>Calculated native +0x3E percentage used by EiMagic; absent from compact values.</summary>
    public short? CastSpeed => IsFull ? BinaryPrimitives.ReadInt16LittleEndian(FullBlock08[54..]) : null;
    /// <summary>Calculated +0x40 percentage, shared by walk/run; absent from compact packets.</summary>
    public short? MovementSpeedPercent => IsFull ? BinaryPrimitives.ReadInt16LittleEndian(FullBlock08[56..]) : null;
    public ushort RemainingSkillPoints => Word42;
    public int MaximumHitPoints => BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(GoldCreatureValuesLayout.MaximumHitPointsOffset));
    public int CurrentHitPoints => BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(GoldCreatureValuesLayout.CurrentHitPointsOffset));
    public ReadOnlySpan<byte> Block24 => _payload.AsSpan(FieldOffset(0x24, GoldCreatureValuesLayout.CompactBlock24Offset), 8);
    public ReadOnlySpan<byte> Block2C => _payload.AsSpan(FieldOffset(0x2C, GoldCreatureValuesLayout.CompactBlock2COffset), 8);
    public uint Word46 => Word(FieldOffset(0x46, GoldCreatureValuesLayout.CompactWord46Offset));
    public ushort Word56 => Short(FieldOffset(0x56, GoldCreatureValuesLayout.CompactWord56Offset));
    public ushort Count22 => _bounds.Count22;
    public ushort Count38 => _bounds.Count38;
    public ReadOnlySpan<byte> Records22 => _payload.AsSpan(_bounds.Records22, Count22 * GoldCreatureValuesLayout.Record22Size);
    public ReadOnlySpan<byte> Records38 => _payload.AsSpan(_bounds.Records38, Count38 * GoldCreatureValuesLayout.Record38Size);
    public ushort Word42 => Short(IsFull ? FieldOffset(0x42, 0) : _bounds.Suffix);
    public ushort Word44 => Short(IsFull ? FieldOffset(0x44, 0) : _bounds.Suffix + 2);
    public ReadOnlySpan<byte> Block1C => _payload.AsSpan(IsFull ? FieldOffset(0x1C, 0) : _bounds.Suffix + 4, 8);
    public byte Byte58 => _payload[IsFull ? FieldOffset(0x58, 0) : _bounds.Suffix + 12];
    public uint Word148 => Word(IsFull ? _bounds.Suffix : _bounds.Suffix + 13);
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(SerializedSize);

    public ReadOnlySpan<byte> GetRecord22(int index) => Record(Records22, index, Count22, GoldCreatureValuesLayout.Record22Size);
    public ReadOnlySpan<byte> GetRecord38(int index) => Record(Records38, index, Count38, GoldCreatureValuesLayout.Record38Size);

    public static GoldCreatureValuesState FromPayload(ReadOnlySpan<byte> payload, ushort opcode)
    {
        var full = GoldCreatureValuesLayout.IsFull(opcode);
        var bounds = GoldCreatureValuesBounds.Read(payload, full);
        return new(payload.ToArray(), bounds, full);
    }

    public byte[] ToPayload() => _payload.ToArray();

    public GoldCreatureValuesState WithCurrentHitPoints(int hitPoints)
    {
        var copy = _payload.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(copy.AsSpan(GoldCreatureValuesLayout.CurrentHitPointsOffset), hitPoints);
        return new(copy, _bounds, IsFull);
    }

    private int FieldOffset(int nativeOffset, int compactOffset) => IsFull
        ? GoldCreatureValuesLayout.FullBlock08Offset + nativeOffset - 8 : compactOffset;
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));
    private ushort Short(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(offset));
    private static ReadOnlySpan<byte> Record(ReadOnlySpan<byte> records, int index, int count, int size)
    {
        if ((uint)index >= (uint)count) throw new ArgumentOutOfRangeException(nameof(index));
        return records.Slice(index * size, size);
    }
}
