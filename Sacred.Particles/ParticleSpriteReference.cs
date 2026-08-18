namespace Sacred.Particles;

/// <summary>
/// Describes an original Texture.pak particle atlas independently of a renderer.
/// </summary>
public readonly record struct ParticleSpriteReference(
    string TextureName,
    int AtlasColumns,
    int AtlasRows,
    int FrameCount,
    float FrameDurationSeconds,
    ParticleShaderKind Shader);
