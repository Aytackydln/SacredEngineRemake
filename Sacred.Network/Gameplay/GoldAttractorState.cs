namespace Sacred.Network.Gameplay;

/// <summary>Lossless native AI attractor record. Operand meanings and AI execution remain separate from receipt.</summary>
public sealed class GoldAttractorState
{
    private readonly byte[] _data;
    private GoldAttractorState(ReadOnlySpan<byte> data) => _data = data.ToArray();
    public byte[] ToPayload() => _data.ToArray();
    public static GoldAttractorState FromPayload(ReadOnlySpan<byte> data)
    {
        if (data.Length != GoldScriptSpatialLayout.Size) throw new InvalidDataException("Gold attractors require 24 bytes.");
        return new(data);
    }
}
