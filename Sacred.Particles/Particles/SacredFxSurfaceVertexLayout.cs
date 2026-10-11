using System.Numerics;
using System.Runtime.InteropServices;

namespace Sacred.Particles.Particles;

/// <summary>Gold FVF 0x1E2 vertex used by FX triangle strips (32 bytes).</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = SerializedSize)]
public readonly struct SacredFxSurfaceVertexLayout
{
    public const int SerializedSize = 0x20;
    [FieldOffset(0x00)] public readonly Vector3 Position;
    [FieldOffset(0x0C)] public readonly float PointSize;
    [FieldOffset(0x10)] public readonly uint Color;
    [FieldOffset(0x14)] public readonly uint Specular;
    [FieldOffset(0x18)] public readonly Vector2 TextureCoordinate;

    private SacredFxSurfaceVertexLayout(Vector3 position, float pointSize, uint color, uint specular, Vector2 uv)
    { Position = position; PointSize = pointSize; Color = color; Specular = specular; TextureCoordinate = uv; }
    public SacredFxSurfaceVertexLayout WithPosition(Vector3 position) => new(position, PointSize, Color, Specular, TextureCoordinate);
    public SacredFxSurfaceVertexLayout WithColor(uint color) => new(Position, PointSize, color, Specular, TextureCoordinate);
}
