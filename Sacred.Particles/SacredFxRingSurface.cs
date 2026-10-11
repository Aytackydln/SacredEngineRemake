using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>A complete ring submission. Every segment repeats a whole texture, with the original stored trig intermediates.</summary>
public sealed record SacredFxRingSurface(SacredFxRingDefinition Definition, int SegmentCount,
    float InnerRadius, float OuterRadius, uint Color, Vector3 Offset)
{
    public SacredFxSurfaceVertexLayout Vertex(int segment, int corner)
    {
        if ((uint)segment >= SegmentCount || (uint)corner >= 4) throw new ArgumentOutOfRangeException(nameof(segment));
        var point = segment + corner / 2;
        var angle = point == 0 ? Definition.InitialAngle : point * (double)Definition.FullAngle / (float)SegmentCount;
        var radius = corner % 2 == 0 ? OuterRadius : InnerRadius;
        // Outer coordinates retain x87 precision; inner X reloads sin's float store after the first point.
        var sine = Math.Sin(angle); var cosine = Math.Cos(angle);
        var x = (float)((point == 0 || corner % 2 == 0 ? sine : (float)sine) * radius);
        var y = (float)((point == 0 && corner % 2 != 0 ? (float)cosine : cosine) * radius);
        return Definition.Vertices[corner].WithPosition(new Vector3(x,y,0)).WithColor(Color);
    }
    public Matrix4x4 Matrix => Matrix4x4.CreateTranslation(Offset);
}
