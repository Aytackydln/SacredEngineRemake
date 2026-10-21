using System.Numerics;

namespace Sacred.Particles;

/// <summary>Native stdLensflare/stdFlare billboard, optionally faded by its updater's age.</summary>
public sealed record SacredParticleHaloDefinition(string TextureName, float HalfSize, uint Color,
    uint NativeAddress)
{
    /// <summary>Native render state 8 selects ONE when set, SRC_ALPHA when clear.</summary>
    public bool SourceColorOnly { get; init; }
    /// <summary>stdFlare attachment argument; -1 selects base position, nonnegative values
    /// select a base-system attachment point. Null identifies a lensflare call without this argument.</summary>
    public int? AttachmentIndex { get; init; }
    /// <summary>Native local offset passed to stdLensflare/stdFlare.</summary>
    public Vector3 Offset { get; init; }
    public bool Additive { get; init; } = true;
    /// <summary>Native lensflare quad is transformed only in world XY.</summary>
    public bool GroundAligned { get; init; }
    public int AtlasSide { get; init; } = 1;
    public float Rotation { get; init; }
    public float RotationRate { get; init; }
    public float SizeGrowthRate { get; init; }
    public float SineSizeAmplitude { get; init; }
    public SacredParticleHaloSizeCurve? SizeCurve { get; init; }
    public float? VisibleFromSeconds { get; init; }
    public float? VisibleUntilSeconds { get; init; }
    public SacredParticleHaloEnvelope? Envelope { get; init; }
    public float RotationAt(float age) => (float)(Rotation + age * (double)RotationRate);
    public SacredParticleAtlasAnimation? AtlasAnimation { get; init; }
    /// <summary>The renderer continues this finite animation while the emitter's particle vector drains.</summary>
    public bool PersistAfterEmission { get; init; }
    public float? FadeStartSeconds { get; init; }
    public float? FadeEndSeconds { get; init; }
    public float FadeAlphaScale { get; init; } = 255;
    public float? PulseAngularFrequency { get; init; }
    public float PulseAmplitude { get; init; }
    public float HalfSizeAt(float age) => SizeCurve is { } curve ? curve.At(age) : PulseAngularFrequency is { } frequency
        ? (float)(HalfSize + (double)(float)((Math.Sin((double)age * frequency) + 1) * 0.5) * PulseAmplitude) : (float)(HalfSize + age * (double)SizeGrowthRate + Math.Sin(age) * SineSizeAmplitude);
    public uint ColorAt(float age)
    {
        if (FadeStartSeconds is not { } start || FadeEndSeconds is not { } end || age < start) return Color;
        var alpha = age >= end ? 0 : (int)((end - (double)age) / (end - (double)start) * FadeAlphaScale);
        return (Color & 0xFFFFFFu) | ((uint)Math.Clamp(alpha, 0, 255) << 24);
    }
}
