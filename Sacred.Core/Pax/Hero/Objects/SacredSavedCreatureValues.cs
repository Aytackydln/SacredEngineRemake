using System.Buffers.Binary;

namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Lossless file values with three ushort-counted lists; not the network compact/full value stream.</summary>
public sealed class SacredSavedCreatureValues
{
    private readonly byte[] _payload;
    private readonly int _records38Offset;
    private readonly int _records28Offset;
    private SacredSavedCreatureValues(byte[] payload, int n, int m, int k, int records38, int records28)
        => (_payload, Count22, Count38, Count28, _records38Offset, _records28Offset) = (payload, n, m, k, records38, records28);
    public uint Marker => BinaryPrimitives.ReadUInt32LittleEndian(_payload);
    public int Count22 { get; }
    public int Count38 { get; }
    public int Count28 { get; }
    public int SerializedSize => _payload.Length;
    public ReadOnlySpan<byte> Block128 => _payload.AsSpan(4, 24);
    public ReadOnlySpan<byte> Block08 => _payload.AsSpan(28, 238);
    public int MaximumHitPoints => BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureValuesLayout.MaximumHitPointsOffset));
    public int CurrentHitPoints => BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureValuesLayout.CurrentHitPointsOffset));
    public uint Experience => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureValuesLayout.ExperienceOffset));
    public uint Gold => BinaryPrimitives.ReadUInt32LittleEndian(_payload.AsSpan(SacredSavedCreatureValuesLayout.GoldOffset));
    public ushort Level => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(SacredSavedCreatureValuesLayout.LevelOffset));
    public ushort RemainingSkillPoints => BinaryPrimitives.ReadUInt16LittleEndian(_payload.AsSpan(SacredSavedCreatureValuesLayout.RemainingSkillPointsOffset));
    public ReadOnlySpan<byte> GetRecord22(int index) => SacredSavedStreamBounds.Record(_payload, index, Count22, 268, 22);
    public ReadOnlySpan<byte> GetRecord38(int index) => SacredSavedStreamBounds.Record(_payload, index, Count38, _records38Offset, 38);
    public ReadOnlySpan<byte> GetRecord28(int index) => SacredSavedStreamBounds.Record(_payload, index, Count28, _records28Offset, 28);
    public byte[] ToPayload() => _payload.ToArray();
    public static SacredSavedCreatureValues FromPayload(ReadOnlySpan<byte> payload, byte version)
    {
        SacredSavedStreamBounds.RequireVersion(version);
        SacredSavedStreamBounds.Require(payload, SacredSavedCreatureValuesLayout.MinimumSize);
        var position = SacredSavedCreatureValuesLayout.Count22Offset;
        var n = SacredSavedStreamBounds.List(payload, ref position, 2, 22);
        var records38 = position + 2;
        var m = SacredSavedStreamBounds.List(payload, ref position, 2, 38);
        var records28 = position + 2;
        var k = SacredSavedStreamBounds.List(payload, ref position, 2, 28);
        return new(payload[..position].ToArray(), n, m, k, records38, records28);
    }
}
