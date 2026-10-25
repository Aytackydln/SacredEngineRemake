using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>One live particle produced from an original executable parameter block.</summary>
public readonly record struct WorldParticle(
    int EmitterScriptOffset,
    ParticleSpriteReference Sprite,
    float WorldX,
    float WorldY,
    float Height,
    float Size,
    float Opacity,
    int DrawOrder)
{
    public uint Color { get; init; } = uint.MaxValue;
    /// <summary>Native Gouraud corner colours, when the draw uses fixed per-corner RGB.</summary>
    public ParticleCornerColors? CornerColors { get; init; }
    public int AtlasCell { get; init; }
    public float Rotation { get; init; }
    public float RenderHeight { get; init; }
    /// <summary>Rotate in native world XY before applying the ground camera compression.</summary>
    public bool GroundAligned { get; init; }
    /// <summary>Rotation carries screen dy/dx for projected vertical FX strips.</summary>
    public bool Sheared { get; init; }
    public bool Untextured { get; init; }
    /// <summary>Health fan coverage, clockwise from native world +Y. Rotation carries its fraction.</summary>
    public bool HealthArc { get; init; }
    /// <summary>Ground UI retains authored UI brightness rather than particle amplification.</summary>
    public bool UiBrightness { get; init; }
    /// <summary>Interpolate physical depth across the horizontal world plane.</summary>
    public bool GroundPlaneDepth { get; init; }
    /// <summary>Ground artwork draws after floors and before scenery/creatures, independently of particle backend.</summary>
    public bool GroundDecal { get; init; }
    public bool Additive { get; init; }
    public bool SourceColorOnly { get; init; }
    /// <summary>Camera-depth key relative to the emitter's authored tile, including live motion and height.</summary>
    public float? PainterDepthKey { get; init; }
}
