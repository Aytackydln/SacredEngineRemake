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
    public int AtlasCell { get; init; }
    public float Rotation { get; init; }
    public float RenderHeight { get; init; }
    public bool Additive { get; init; }
    public bool SourceColorOnly { get; init; }
}
