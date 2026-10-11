using System.Numerics;

namespace Sacred.Particles;

public sealed record SacredPulseFlareSurface(SacredFxFlareLayer Layer, bool Billboard, float Angle = 0)
{
    public Matrix4x4 Matrix(Matrix4x4 camera) => Billboard
        ? new SacredSpriteFlareSurface(Layer, true).Matrix(camera)
        : new SacredControlledFlareSurface(Layer, Angle).Matrix;
    public Vector3 Vertex(int corner, Matrix4x4 camera) => Billboard
        ? new SacredSpriteFlareSurface(Layer, true).Vertex(corner, camera)
        : new SacredControlledFlareSurface(Layer, Angle).Vertex(corner);
    public Vector2 TextureCoordinate(int corner) => Billboard
        ? new SacredSpriteFlareSurface(Layer, true).TextureCoordinate(corner)
        : new SacredControlledFlareSurface(Layer, Angle).TextureCoordinate(corner);
}
