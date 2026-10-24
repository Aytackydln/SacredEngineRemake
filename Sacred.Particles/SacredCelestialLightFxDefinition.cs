using System.Runtime.InteropServices;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Gold holylight: an untextured column, two edge strips, ground flares and shrinking births.</summary>
public sealed record SacredCelestialLightFxDefinition(IReadOnlyList<SacredFxSurfaceVertexLayout> ColumnVertices,
    IReadOnlyList<SacredFxSurfaceVertexLayout> EdgeVertices, string GlowTexture, string GroundTexture,
    float WaveRate, float WaveScale, float WaveOrigin, float RetirementFactor,
    float ColumnAlpha, float EdgeAlpha, float GroundHalfSize, float GlowHalfSize, float GroundRotationRate,
    uint GroundRgb, uint GlowRgb, float InitialLifetime)
{
    public bool FlareAdditive { get; init; }
    public bool FlareSourceColorOnly { get; init; }
    public SacredParticleParameterSet BirthParameters(SacredParticleParameterSet parameters, float factor)
    {
        var emission = parameters.Emission;
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref emission, 1));
        foreach (var offset in new[] { 0x08, 0x0C, 0x44, 0x48 })
        {
            var original = BitConverter.ToSingle(bytes.Slice(offset, 4));
            BitConverter.TryWriteBytes(bytes.Slice(offset, 4), original * factor);
        }
        return parameters with { Emission = emission };
    }
}
