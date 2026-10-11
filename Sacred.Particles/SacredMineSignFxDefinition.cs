using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

public sealed record SacredMineSignFxDefinition
{
    public SacredMineSignFxStateLayout InitialState { get; init; }
    public float BirthRate { get; init; }
    public float MaximumCarry { get; init; }
    public float BirthUnit { get; init; }
    public float RingRadius { get; init; }
    public float FullAngle { get; init; }
    public float RandomScale { get; init; }
    public float RotationWidth { get; init; }
    public float RotationHalfWidth { get; init; }
    public float RotationBase { get; init; }
    public float AudioPitchMinimum { get; init; }
    public ushort MineUsedFlag { get; init; }

    /// <summary>Gold 0x4D26E0 (indices 0, 1), followed by the initializer's
    /// low-word mask and event multiplier. Inputs come from the combat-art record.</summary>
    public static uint CalculateMineCount(SacredMineSignFxCombatArtLayout art, ushort level, float multiplier)
    {
        var numerator = unchecked((int)((uint)art.NumeratorBase + (uint)art.NumeratorPerLevel * level));
        var denominator = unchecked((int)((uint)art.DenominatorBase + (uint)art.DenominatorPerLevel * level));
        var ratio = denominator == 0 ? numerator : numerator / (double)denominator;
        var lowWord = unchecked((ushort)(long)Math.Truncate(ratio));
        if (art.NumeratorCap != 0 && lowWord > art.NumeratorCap) lowWord = art.NumeratorCap;
        return unchecked((uint)(long)Math.Truncate(lowWord * (double)multiplier));
    }

    internal SacredParticleEmissionLayout WithBirth(SacredParticleEmissionLayout emission, float x, float y, float rotation)
    {
        Span<byte> bytes = stackalloc byte[SacredParticleEmissionLayout.SerializedSize];
        MemoryMarshal.Write(bytes, in emission);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[0x10..], rotation);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[0x38..], x);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[0x3C..], y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[0x40..], 0);
        return MemoryMarshal.Read<SacredParticleEmissionLayout>(bytes);
    }
}
