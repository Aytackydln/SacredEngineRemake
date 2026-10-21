using System.Buffers.Binary;

namespace Sacred.Network.Gameplay;

/// <summary>Owned original weather bytes, including Gold's reserved words.</summary>
public sealed class GoldWeatherState
{
    private readonly byte[] _payload;
    private GoldWeatherState(byte[] payload) => _payload = payload;
    public uint Flags => BinaryPrimitives.ReadUInt32LittleEndian(_payload);
    public int RainParticles => ReadInt(GoldWeatherLayout.RainParticlesOffset);
    public int FogOpacity => ReadInt(GoldWeatherLayout.FogOpacityOffset);
    public int SnowParticles => ReadInt(GoldWeatherLayout.SnowParticlesOffset);
    public float QuakeStrength => BinaryPrimitives.ReadSingleLittleEndian(_payload.AsSpan(GoldWeatherLayout.QuakeStrengthOffset));
    public float LavaBurstTimer => BinaryPrimitives.ReadSingleLittleEndian(_payload.AsSpan(GoldWeatherLayout.LavaBurstTimerOffset));
    public static GoldWeatherState FromPayload(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != GoldWeatherLayout.Size) throw new ArgumentException("Gold weather requires 32 bytes.", nameof(payload));
        return new(payload.ToArray());
    }
    public byte[] ToPayload() => _payload.ToArray();
    private int ReadInt(int offset) => BinaryPrimitives.ReadInt32LittleEndian(_payload.AsSpan(offset));
}
