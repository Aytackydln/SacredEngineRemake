using System.Buffers.Binary;
using System.Numerics;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless cObjectFX state, independent of cObject3D and cItemBase.</summary>
public sealed class GoldObjectFxState
{
    private readonly byte[] _payload;
    private GoldObjectFxState(byte[] payload) => _payload = payload;
    public GoldObjectBaseState BaseState => GoldObjectBaseState.FromPayload(_payload);
    public uint FxType => Word(GoldObjectFxStateLayout.FxTypeOffset);
    public ushort Flags => Half(GoldObjectFxStateLayout.FlagsOffset);
    /// <summary>Gold receive/stop 0x5A3861/0x5A61F8: the particle system is live.</summary>
    public bool IsSystemActive => (Flags & 1) != 0;
    public ushort Life => Half(GoldObjectFxStateLayout.LifeOffset);
    public uint CasterReference => Word(GoldObjectFxStateLayout.CasterReferenceOffset);
    public byte CasterInstance => _payload[GoldObjectFxStateLayout.CasterInstanceOffset];
    public uint DestinationReference => Word(GoldObjectFxStateLayout.DestinationReferenceOffset);
    public byte DestinationInstance => _payload[GoldObjectFxStateLayout.DestinationInstanceOffset];
    public uint WearerReference => Word(GoldObjectFxStateLayout.WearerReferenceOffset);
    public ushort AttachBone => Half(GoldObjectFxStateLayout.AttachBoneOffset);
    public IReadOnlyList<ushort> AdditionalBones => Enumerable.Range(0, 3)
        .Select(i => Half(GoldObjectFxStateLayout.AdditionalBonesOffset + i * 2)).ToArray();
    public Vector3 MoveDirection => Vector(GoldObjectFxStateLayout.MoveDirectionOffset);
    public float Duration => Float(GoldObjectFxStateLayout.DurationOffset);
    public Vector3 WearingOffset => Vector(GoldObjectFxStateLayout.WearingOffsetOffset);
    public uint IntegerData1 => Word(GoldObjectFxStateLayout.IntegerDataOffset);
    public uint IntegerData2 => Word(GoldObjectFxStateLayout.IntegerDataOffset + 4);
    public uint IntegerData3 => Word(GoldObjectFxStateLayout.IntegerDataOffset + 8);
    public float FloatData1 => Float(GoldObjectFxStateLayout.FloatDataOffset);
    public float FloatData2 => Float(GoldObjectFxStateLayout.FloatDataOffset + 4);
    public float FloatData3 => Float(GoldObjectFxStateLayout.FloatDataOffset + 8);
    public float FloatData4 => Float(GoldObjectFxStateLayout.FloatDataOffset + 12);
    public Vector3 VectorData1 => Vector(GoldObjectFxStateLayout.VectorData1Offset);
    public Vector3 VectorData2 => Vector(GoldObjectFxStateLayout.VectorData2Offset);
    public ReadOnlyMemory<byte> EventData => _payload.AsMemory(GoldObjectFxStateLayout.EventDataOffset, GoldObjectFxStateLayout.EventDataSize);
    public float RemainingRecharge => Float(GoldObjectFxStateLayout.RemainingRechargeOffset);
    public uint WordDc => Word(GoldObjectFxStateLayout.WordDcOffset);
    public uint WordE0 => Word(GoldObjectFxStateLayout.WordE0Offset);
    public IReadOnlyList<uint> References => Enumerable.Range(0, checked((int)Word(GoldObjectFxStateLayout.ReferenceCountOffset)))
        .Select(i => Word(GoldObjectFxStateLayout.ReferencesOffset + i * 4)).ToArray();
    public byte[] ToPayload() => _payload.ToArray();
    public static GoldObjectFxState FromPayload(ReadOnlySpan<byte> payload)
    {
        _ = GoldObjectBaseState.FromPayload(payload);
        if (payload.Length < GoldObjectFxStateLayout.MinimumSize ||
            BinaryPrimitives.ReadUInt32LittleEndian(payload[GoldObjectFxStateLayout.MarkerOffset..]) != GoldObjectFxStateLayout.Marker)
            throw new InvalidDataException("Truncated or invalid Gold cObjectFX state.");
        var count = BinaryPrimitives.ReadUInt32LittleEndian(payload[GoldObjectFxStateLayout.ReferenceCountOffset..]);
        if (count > (uint)((payload.Length - GoldObjectFxStateLayout.ReferencesOffset) / 4))
            throw new InvalidDataException("Truncated Gold cObjectFX reference vector.");
        // Native object packets can include unused capacity after the serialized object.
        return new(payload.ToArray());
    }
    private uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(offset));
    private ushort Half(int offset) => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(offset));
    private float Float(int offset) => BitConverter.UInt32BitsToSingle(Word(offset));
    private Vector3 Vector(int offset) => new(Float(offset), Float(offset + 4), Float(offset + 8));
}
