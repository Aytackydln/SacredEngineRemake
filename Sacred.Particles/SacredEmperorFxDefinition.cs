using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Decoded Emperor particle controller and render-time randomized stdFlare.</summary>
public sealed record SacredEmperorFxDefinition(string FlareTexture)
{
    public SacredEmperorFxStateLayout InitialState { get; init; }
    public float RandomScale { get; init; }
    public float FlareSizeMinimum { get; init; }
    public float FlareSizeWidth { get; init; }
    public uint FlareColor { get; init; }
    public bool FlareSourceColorOnly { get; init; }
    public float RadiusRate { get; init; }
    public uint InitialRadius { get; init; }
    public uint InitialNotification { get; init; }
    public ushort HitFlag { get; init; }
    public float HitExtension { get; init; }
    public float HitFlareMultiplier { get; init; }
    public float HitSizeMultiplier { get; init; }
    public uint HitNotification { get; init; }
    public uint SpecialHitNotification { get; init; }

    internal SacredParticleEmissionLayout Resize(SacredParticleEmissionLayout emission)
    {
        Span<byte> bytes = stackalloc byte[SacredParticleEmissionLayout.SerializedSize];
        MemoryMarshal.Write(bytes, in emission);
        BinaryPrimitives.WriteSingleLittleEndian(bytes[8..], (float)(emission.Size * (double)HitSizeMultiplier));
        BinaryPrimitives.WriteSingleLittleEndian(bytes[12..], (float)(emission.SizeRandomWidth * (double)HitSizeMultiplier));
        return MemoryMarshal.Read<SacredParticleEmissionLayout>(bytes);
    }
}
