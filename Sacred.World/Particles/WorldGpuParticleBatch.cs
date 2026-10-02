using System.Numerics;
using Sacred.Particles;

namespace Sacred.World.Particles;

/// <summary>Decoded emitter draw metadata; persistent simulation state is renderer-owned.</summary>
public sealed record WorldGpuParticleBatch(ParticleGpuBatch Batch, SacredParticleDefinition Definition,
    ParticleSpriteReference Sprite, SacredParticleProjection Projection, Vector2 Origin,
    Vector2 DepthAnchor, float HeightOffset);
