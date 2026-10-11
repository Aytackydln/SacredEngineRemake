using System.Numerics;

namespace Sacred.Particles;

public sealed record SacredControlledFlareSurface(SacredFxFlareLayer Layer, float Angle,
    int AtlasCell = 0, int AtlasSide = 1)
{
    public Matrix4x4 Matrix => Matrix4x4.CreateRotationZ(-Angle) * Matrix4x4.CreateTranslation(Layer.Offset);
    public Vector3 Vertex(int corner) => Vector3.Transform(new(corner < 2 ? Layer.HalfSize : -Layer.HalfSize,
        (corner % 2 * 2 - 1) * Layer.HalfSize, 0), Matrix);
    // stdLensflare's atlas branch reverses both axes relative to its whole-texture defaults.
    public Vector2 TextureCoordinate(int corner) => AtlasSide == 1
        ? new(corner < 2 ? 1 : 0, corner % 2)
        : new((AtlasCell % AtlasSide + (corner < 2 ? 0 : 1)) / (float)AtlasSide,
            (AtlasCell / AtlasSide + 1 - corner % 2) / (float)AtlasSide);
}
