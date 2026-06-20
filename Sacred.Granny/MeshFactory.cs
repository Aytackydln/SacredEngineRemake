using System.Numerics;

namespace Sacred.Granny;

public static class MeshFactory
{
    public static Mesh CreateHumanoidProxyMesh()
    {
        var vertices = new VertexPositionNormalTexture[]
        {
            new(new(-18, 0, 0), Vector3.UnitZ, new(0, 1)),
            new(new(18, 0, 0), Vector3.UnitZ, new(1, 1)),
            new(new(18, 0, 70), Vector3.UnitZ, new(1, 0)),
            new(new(-18, 0, 70), Vector3.UnitZ, new(0, 0)),

            new(new(-28, 0, 70), Vector3.UnitZ, new(0, 1)),
            new(new(28, 0, 70), Vector3.UnitZ, new(1, 1)),
            new(new(20, 0, 115), Vector3.UnitZ, new(1, 0)),
            new(new(-20, 0, 115), Vector3.UnitZ, new(0, 0)),

            new(new(-14, 0, 115), Vector3.UnitZ, new(0, 1)),
            new(new(14, 0, 115), Vector3.UnitZ, new(1, 1)),
            new(new(14, 0, 145), Vector3.UnitZ, new(1, 0)),
            new(new(-14, 0, 145), Vector3.UnitZ, new(0, 0)),
        };

        ushort[] indices =
        [
            0, 1, 2, 0, 2, 3,
            4, 5, 6, 4, 6, 7,
            8, 9, 10, 8, 10, 11
        ];

        return new Mesh(vertices, indices);
    }

    public static Mesh CreateShieldTowerProxyMesh()
    {
        var vertices = new VertexPositionNormalTexture[]
        {
            new(new(-35, -35, 0), Vector3.UnitZ, new(0, 1)),
            new(new(35, -35, 0), Vector3.UnitZ, new(1, 1)),
            new(new(35, 35, 0), Vector3.UnitZ, new(1, 0)),
            new(new(-35, 35, 0), Vector3.UnitZ, new(0, 0)),
            new(new(-28, -28, 120), Vector3.UnitZ, new(0, 1)),
            new(new(28, -28, 120), Vector3.UnitZ, new(1, 1)),
            new(new(28, 28, 120), Vector3.UnitZ, new(1, 0)),
            new(new(-28, 28, 120), Vector3.UnitZ, new(0, 0)),
            new(new(0, 0, 170), Vector3.UnitZ, new(0.5f, 0))
        };

        ushort[] indices =
        [
            0, 1, 2, 0, 2, 3,
            0, 4, 5, 0, 5, 1,
            1, 5, 6, 1, 6, 2,
            2, 6, 7, 2, 7, 3,
            3, 7, 4, 3, 4, 0,
            4, 8, 5, 5, 8, 6, 6, 8, 7, 7, 8, 4
        ];

        return new Mesh(vertices, indices);
    }
}