using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>Original glyph strip expansion uses the fixed native view-side vector, not a moving preview camera.</summary>
internal static class SacredGlyphRibbonGeometry
{
    public static void Write(in SacredUnderworldGlyphFxStateLayout state, SacredUnderworldGlyphFxDefinition definition,
        Span<SacredFxSurfaceVertexLayout> vertices)
    {
        var points = SacredUnderworldGlyphFxStateLayout.PointsPerWave;
        for (var wave = 0; wave < SacredUnderworldGlyphFxStateLayout.WaveCount; wave++)
        {
            float sideX = 0, sideY = 0;
            double sideZ = 0;
            for (var point = 0; point < points; point++)
            {
                var center = state.Points[wave * points + point];
                if (point < points - 1)
                {
                    var next = state.Points[wave * points + point + 1];
                    var dx = next.X - (double)center.X;
                    var dy = next.Y - (double)center.Y;
                    var dz = next.Z - (double)center.Z;
                    var x = (float)(dy * definition.RibbonSideY - dz * definition.RibbonSideZ);
                    var y = (float)dx;
                    var z = dx * definition.RibbonSideZ;
                    var length = Math.Sqrt(z * z + y * (double)y + x * (double)x);
                    if (length < definition.RibbonNormalMinimum) { sideX = 0; sideY = 0; sideZ = 0; }
                    else { var inverse = 1 / length; sideX = (float)(x * inverse); sideY = (float)(y * inverse); sideZ = z * inverse; }
                }
                // Last point deliberately retains the preceding segment's normalized side.
                for (var edge = 0; edge < 2; edge++)
                {
                    var sign = edge == 0 ? 1 : -1;
                    var position = new Vector3(
                        (float)(center.X + sign * sideX * (double)state.Thickness * definition.RibbonHalfWidth),
                        (float)(center.Y + sign * sideY * (double)state.Thickness * definition.RibbonHalfWidth),
                        (float)(center.Z + sign * sideZ * state.Thickness * definition.RibbonHalfWidth));
                    var index = point * 2 + edge;
                    vertices[wave * points * 2 + index] = definition.RibbonVertices[index].WithPosition(position);
                }
            }
        }
    }
}
