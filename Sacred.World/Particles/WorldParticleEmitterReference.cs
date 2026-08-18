using System.Numerics;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Placement and light authored for a particle attached to a world object.</summary>
public readonly record struct WorldParticleEmitterReference(
    ParticleSpriteReference Sprite,
    float OffsetX,
    float OffsetY,
    float Width,
    float Height,
    Vector3 LightColour,
    float LightDiameter,
    float LightOpacity,
    bool TransposeTexture = false);
