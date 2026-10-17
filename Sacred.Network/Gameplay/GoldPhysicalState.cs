using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Physical stream prefix with native memory-offset names for unresolved fields.</summary>
public sealed class GoldPhysicalState
{
    private readonly byte[] _payload;

    private GoldPhysicalState(byte[] payload) => _payload = payload;

    public GoldObjectBaseState BaseState => GoldObjectBaseState.FromPayload(_payload);
    public uint Word64 => Word(GoldPhysicalStateLayout.Word64Offset);
    public uint WordF0 => Word(GoldPhysicalStateLayout.WordF0Offset);
    public uint Word60 => Word(GoldPhysicalStateLayout.Word60Offset);
    public uint Word68 => Word(GoldPhysicalStateLayout.Word68Offset);
    public uint Word6C => Word(GoldPhysicalStateLayout.Word6COffset);
    public GoldPhysicalVector Vector70 => Vector(GoldPhysicalStateLayout.Vector70Offset);
    public GoldPhysicalVector VectorD8 => Vector(GoldPhysicalStateLayout.VectorD8Offset);
    public GoldPhysicalVector VectorE4 => Vector(GoldPhysicalStateLayout.VectorE4Offset);
    public ushort Word94 => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(GoldPhysicalStateLayout.Word94Offset));
    public ushort Word96 => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(GoldPhysicalStateLayout.Word96Offset));
    public uint WordB0 => Word(GoldPhysicalStateLayout.WordB0Offset);
    public GoldPhysicalVector SelectedVector => Vector(GoldPhysicalStateLayout.SelectedVectorOffset);
    public bool UsesVectorCC => (BaseState.Flags & GoldPhysicalStateLayout.VectorCCFlag) != 0;
    public ReadOnlySpan<byte> RemainingData => _payload.AsSpan(GoldPhysicalStateLayout.Size);

    public static GoldPhysicalState FromPayload(ReadOnlySpan<byte> payload)
    {
        Validate(payload);
        return new(payload.ToArray());
    }

    /// <summary>Retain exact floating-point bits, lower blocks and all following subtype bytes.</summary>
    public byte[] ToPayload() => _payload.ToArray();

    public GoldPhysicalState WithAnimation(uint animation)
    {
        var bytes = ToPayload();
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(GoldPhysicalStateLayout.Word64Offset), animation);
        return new(bytes);
    }

    internal static void Validate(ReadOnlySpan<byte> payload)
    {
        _ = GoldObjectBaseState.FromPayload(payload);
        if (payload.Length < GoldPhysicalStateLayout.Size)
            throw new InvalidDataException("Gold physical object state requires at least 121 bytes.");
        if (!GoldObjectStateReader.HasMarker(payload, GoldPhysicalStateLayout.MarkerOffset, GoldPhysicalStateLayout.Marker))
            throw new InvalidDataException("Unsupported Gold physical object state marker.");
    }

    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));
    private GoldPhysicalVector Vector(int offset) => GoldPhysicalVector.Read(_payload.AsSpan(offset));
}
