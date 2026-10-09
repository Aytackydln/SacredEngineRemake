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
    public int AtlasSide { get; init; } = 1;
    public float Rotation { get; init; }
    public SacredParticleAtlasAnimation? AtlasAnimation { get; init; }
    /// <summary>The renderer continues this finite animation while the emitter's particle vector drains.</summary>
    public bool PersistAfterEmission { get; init; }
    public float? FadeStartSeconds { get; init; }
    public float? FadeEndSeconds { get; init; }
    public float FadeAlphaScale { get; init; } = 255;
    public float? PulseAngularFrequency { get; init; }
    public float PulseAmplitude { get; init; }
    public float HalfSizeAt(float age) => PulseAngularFrequency is { } frequency
        ? (float)(HalfSize + (double)(float)((Math.Sin((double)age * frequency) + 1) * 0.5) * PulseAmplitude) : HalfSize;
    public uint ColorAt(float age)
    {
        if (FadeStartSeconds is not { } start || FadeEndSeconds is not { } end || age < start) return Color;
        var alpha = age >= end ? 0 : (int)((end - (double)age) / (end - (double)start) * FadeAlphaScale);
        return (Color & 0xFFFFFFu) | ((uint)Math.Clamp(alpha, 0, 255) << 24);
    }
}
