using System.Buffers.Binary;
using System.Numerics;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless one-derived-block particle snapshot. This maps the proven
/// envelope and base header without treating remaining particle state as unused
/// packet capacity. Constructing it does not create or update a particle system.</summary>
public sealed class GoldParticleSnapshot
{
    private readonly byte[] _data;
    private readonly int _derivedLength;
    private readonly int _derivedOffset;
    private int BaseOffset => _derivedOffset + _derivedLength + 4;
    private GoldParticleSnapshot(byte[] data, int derivedLength, int derivedOffset)
        => (_data, _derivedLength, _derivedOffset) = (data, derivedLength, derivedOffset);
    public bool HasDerivedMarker => _derivedOffset == GoldParticleSnapshotLayout.DerivedDataOffset;
    public ReadOnlyMemory<byte> DerivedState => _data.AsMemory(_derivedOffset, _derivedLength);
    public ReadOnlyMemory<byte> BaseState => _data.AsMemory(BaseOffset);
    public uint Resource => Word(_data, BaseOffset + GoldParticleSnapshotLayout.BaseResourceOffset);
    public Vector3 Position => new(Float(GoldParticleSnapshotLayout.BasePositionOffset),
        Float(GoldParticleSnapshotLayout.BasePositionOffset + 4), Float(GoldParticleSnapshotLayout.BasePositionOffset + 8));
    public byte[] ToPayload() => _data.ToArray();

    public static GoldParticleSnapshot FromPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4) throw new InvalidDataException("Truncated particle snapshot header.");
        var marked = Word(data, 0) == GoldParticleSnapshotLayout.DerivedMarker;
        var offset = marked ? GoldParticleSnapshotLayout.DerivedDataOffset : GoldParticleSnapshotLayout.SizeOnlyDataOffset;
        var minimum = offset + 4 + GoldParticleSnapshotLayout.BaseHeaderSize;
        if (data.Length < minimum) throw new InvalidDataException("Truncated particle snapshot framing.");
        var length = Word(data, marked ? GoldParticleSnapshotLayout.DerivedSizeOffset : GoldParticleSnapshotLayout.SizeOnlyLengthOffset);
        if (length > data.Length - minimum)
            throw new InvalidDataException("Particle snapshot ends inside its derived state or base header.");
        var end = offset + (int)length;
        if (Word(data, end) != GoldParticleSnapshotLayout.DerivedEndMarker ||
            Word(data, end + 4) != GoldParticleSnapshotLayout.BaseMarker)
            throw new InvalidDataException("Unsupported particle snapshot block markers or nested family.");
        return new(data.ToArray(), (int)length, offset);
    }

    private float Float(int offset) => BitConverter.UInt32BitsToSingle(Word(_data, BaseOffset + offset));
    private static uint Word(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(data[offset..]);
}
