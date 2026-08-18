namespace Sacred.Particles;

/// <summary>Semantic use of the channels in a decoded RGBA particle texture.</summary>
public enum ParticleTextureEncoding
{
    /// <summary>RGB supplies colour and alpha supplies coverage.</summary>
    AlphaColour,

    /// <summary>Alpha supplies coverage while the shader supplies/tints the colour.</summary>
    AlphaMask,

    /// <summary>Black is the zero-energy background and RGB is additively composed.</summary>
    BlackKeyColour,

    /// <summary>An opaque colour image with no particle-specific transparency convention.</summary>
    OpaqueColour
}
