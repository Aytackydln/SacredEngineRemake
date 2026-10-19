using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless creature prefix, including equipment instance refs; variable subtype bytes remain opaque.</summary>
public sealed class GoldCreatureState
{
    private readonly byte[] _payload;
    private readonly uint[] _equipment;

    private GoldCreatureState(byte[] payload)
    {
        _payload = payload;
        _equipment = new uint[GoldCreatureStateLayout.EquipmentCount];
        for (var index = 0; index < _equipment.Length; index++)
            _equipment[index] = Word(GoldCreatureStateLayout.EquipmentOffset + index * 4);
    }

    public GoldObjectBaseState BaseState => GoldObjectBaseState.FromPayload(_payload);
    public GoldPhysicalState PhysicalState => GoldPhysicalState.FromPayload(_payload);
    /// <summary>Native +0x1A4 entries, after the writer's object-manager filtering. Zero entries are retained.</summary>
    public ReadOnlySpan<uint> EquipmentReferences => _equipment;
    public uint Word1F0 => Word(GoldCreatureStateLayout.Word1F0Offset);
    public uint Word1F4 => Word(GoldCreatureStateLayout.Word1F4Offset);
    public uint BehaviorFlags => Word1F4;
    public uint Word1F8 => Word(GoldCreatureStateLayout.Word1F8Offset);
    public byte Byte1FC => _payload[GoldCreatureStateLayout.Byte1FCOffset];
    public byte Byte1FD => _payload[GoldCreatureStateLayout.Byte1FDOffset];
    public ushort Word1FE => Short(GoldCreatureStateLayout.Word1FEOffset);
    public uint Word200 => Word(GoldCreatureStateLayout.Word200Offset);
    public bool UsesAlternateBowProjectile => (Word200 & GoldCreatureStateLayout.AlternateBowProjectileMask) != 0;
    /// <summary>Includes service bits: 0x1000 smith, 0x2000 trader, 0x4000 combo, 0x04000000 horse trader.</summary>
    public uint ServiceFlags => Word200;
    public uint Word245 => Word(GoldCreatureStateLayout.Word245Offset);
    public uint DialogIndex => Word245;
    public uint Word249 => Word(GoldCreatureStateLayout.Word249Offset);
    public uint Word24D => Word(GoldCreatureStateLayout.Word24DOffset);
    public uint Word251 => Word(GoldCreatureStateLayout.Word251Offset);
    public uint Word255 => Word(GoldCreatureStateLayout.Word255Offset);
    public uint Word259 => Word(GoldCreatureStateLayout.Word259Offset);
    public ushort Word25F => Short(GoldCreatureStateLayout.Word25FOffset);
    public ushort Word261 => Short(GoldCreatureStateLayout.Word261Offset);
    /// <summary>Native simulation state; indexes the ALT overhead-icon table at Gold 0x599F7B.</summary>
    public ushort SimulationState => Word261;
    public byte Byte2B6 => _payload[GoldCreatureStateLayout.Byte2B6Offset];
    public ushort Word2B7 => Short(GoldCreatureStateLayout.Word2B7Offset);
    public ReadOnlySpan<byte> Block2B9 => _payload.AsSpan(GoldCreatureStateLayout.Block2B9Offset, GoldCreatureStateLayout.Block2B9Size);
    public uint Word2C9 => Word(GoldCreatureStateLayout.Word2C9Offset);
    public float AnimationSpeedMultiplier => BitConverter.UInt32BitsToSingle(Word2C9);
    public ReadOnlySpan<byte> Block2ED => _payload.AsSpan(GoldCreatureStateLayout.Block2EDOffset, GoldCreatureStateLayout.Block2EDSize);
    /// <summary>Action-bank fields interpreted as index/count only during native path movement.</summary>
    public uint MovementPathIndex => Word(GoldCreatureStateLayout.MovementPathIndexOffset);
    public uint MovementPathCount => Word(GoldCreatureStateLayout.MovementPathCountOffset);
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(GoldCreatureStateLayout.PrefixSize);

    /// <summary>Read the known embedded tail using the original opcode-specific values format.</summary>
    public GoldCreatureTail? ReadTail(ushort opcode) =>
        GoldObjectStateReader.HasMarker(_payload, GoldCreatureTailLayout.Offset, GoldCreatureValuesLayout.Marker)
            ? GoldCreatureTail.FromPayload(RemainingData, opcode) : null;

    public static GoldCreatureState FromPayload(ReadOnlySpan<byte> payload)
    {
        Validate(payload);
        return new(payload.ToArray());
    }

    public byte[] ToPayload() => _payload.ToArray();

    /// <summary>Changes one native equipment instance reference while preserving all other serialized layers.</summary>
    public GoldCreatureState WithEquipmentReference(int slot, uint reference)
    {
        if ((uint)slot >= GoldCreatureStateLayout.EquipmentCount) throw new ArgumentOutOfRangeException(nameof(slot));
        var bytes = ToPayload();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldCreatureStateLayout.EquipmentOffset + slot * 4), reference);
        return new(bytes);
    }

    internal static void Validate(ReadOnlySpan<byte> payload)
    {
        GoldPhysicalState.Validate(payload);
        if (payload.Length < GoldCreatureStateLayout.PrefixSize)
            throw new InvalidDataException("Gold creature object state requires at least 445 bytes.");
        if (!GoldObjectStateReader.HasMarker(payload, GoldCreatureStateLayout.MarkerOffset, GoldCreatureStateLayout.Marker))
            throw new InvalidDataException("Unsupported Gold creature object state marker.");
    }

    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));
    private ushort Short(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(offset));
}
