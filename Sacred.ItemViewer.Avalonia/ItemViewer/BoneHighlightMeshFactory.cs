using System.Numerics;
using Sacred.Granny;

namespace Sacred.ItemViewer.Avalonia.ItemViewer;

internal static class BoneHighlightMeshFactory
{
    public static Mesh Create(Vector3 position, float radius)
    {
        var vertices = new[]
        {
            new VertexPositionNormalTexture(position + Vector3.UnitX * radius, Vector3.UnitX, Vector2.Zero),
            new VertexPositionNormalTexture(position - Vector3.UnitX * radius, -Vector3.UnitX, Vector2.Zero),
            new VertexPositionNormalTexture(position + Vector3.UnitY * radius, Vector3.UnitY, Vector2.Zero),
            new VertexPositionNormalTexture(position - Vector3.UnitY * radius, -Vector3.UnitY, Vector2.Zero),
            new VertexPositionNormalTexture(position + Vector3.UnitZ * radius, Vector3.UnitZ, Vector2.Zero),
            new VertexPositionNormalTexture(position - Vector3.UnitZ * radius, -Vector3.UnitZ, Vector2.Zero)
        };
        ushort[] indices =
        [
            0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4,
            2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5
        ];
        return new Mesh(vertices, indices);
    }
}
