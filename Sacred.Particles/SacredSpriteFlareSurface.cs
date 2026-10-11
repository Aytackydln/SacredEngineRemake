using System.Numerics;

namespace Sacred.Particles;

/// <summary>stdFlare copies the camera basis; stdLensflare uses a ground rotation. Both replace translation.</summary>
public sealed record SacredSpriteFlareSurface(SacredFxFlareLayer Layer, bool Billboard)
{
    public Matrix4x4 Matrix(Matrix4x4 cameraBasis)
    {
        var matrix = Billboard ? cameraBasis : Matrix4x4.Identity;
        matrix.M41 = Layer.Offset.X; matrix.M42 = Layer.Offset.Y; matrix.M43 = Layer.Offset.Z;
        return matrix;
    }
    public Vector3 Vertex(int corner, Matrix4x4 cameraBasis)
    {
        var x = (corner < 2 ? -1 : 1) * Layer.HalfSize * (Billboard ? 1 : -1);
        return Vector3.Transform(new(x, (corner % 2 * 2 - 1) * Layer.HalfSize, 0), Matrix(cameraBasis));
    }
    public Vector2 TextureCoordinate(int corner) => Billboard ? new(corner / 2, 1 - corner % 2)
        : new(corner < 2 ? 1 : 0, corner % 2);
}
