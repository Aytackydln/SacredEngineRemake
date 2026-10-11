using System.Numerics;

namespace Sacred.Particles;

/// <summary>One native stdLensflare ground plane, including its stored-float size/height and blend arguments.</summary>
public sealed record SacredFxFlareLayer(string TextureName, float HalfSize, uint Color,
    Vector3 Offset, float RotationRate, bool SourceColorOnly, bool Additive);
