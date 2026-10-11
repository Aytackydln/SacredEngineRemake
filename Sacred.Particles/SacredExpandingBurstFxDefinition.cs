using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Native ring and two ground layers accompanying the standard particle burst.</summary>
public sealed record SacredExpandingBurstFxDefinition(string RingTexture, string GroundTexture, SacredFxRingDefinition Ring)
{
    public SacredExpandingBurstFxStateLayout InitialState { get; init; }
    public float Lifetime { get; init; }
    public float RingDuration { get; init; }
    public float RingFadeStart { get; init; }
    public float RingFadeRate { get; init; }
    public float RadiusRate { get; init; }
    public uint? EventRadiusRate { get; init; }
    public float RingWidth { get; init; }
    public Vector3 RingColorScale { get; init; }
    public int SegmentCount { get; init; }
    public float GroundTimeRate { get; init; }
    public Vector2 GroundSizes { get; init; }
    public Vector2 GroundRotations { get; init; }
    public uint GroundColor { get; init; }
    public double RenderRadiusDecay { get; init; } = 1;
    public uint InitialNotification { get; init; }
    public uint InitialLightStrength { get; init; }
}
