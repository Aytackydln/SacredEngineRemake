using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

/// <summary>Projects native direct FX quads through the shared Engine/terminal particle contract.</summary>
internal sealed class WorldFxSpriteWriter(WorldAttachedParticleEffect state, SacredParticleProjection projection,
    List<WorldParticle> output)
{
    private int _order;
    public void Add(string texture, float halfWidth, float halfHeight, uint color, Vector3 offset, bool ground,
        float rotation = 0, ParticleCornerColors? corners = null, bool untextured = false, float? shear = null,
        bool additive = true, bool sourceColorOnly = false)
    {
        var point = state.Position + IsometricProjection.IsoToWorld(projection.Project(new(offset.X, offset.Y, 0)));
        var height = (state.Height + offset.Z) * projection.HeightFactor * projection.VerticalScale;
        var slope = shear * projection.GroundDepthFactor * projection.VerticalScale / projection.HorizontalScale;
        output.Add(new(unchecked((int)state.Generation), new(texture, 1, 1, 1, 1, ParticleShaderKind.ItemParticle),
            point.X, point.Y, height, halfWidth * 2 * projection.HorizontalScale, (color >> 24) / 255f, _order++)
        {
            RenderHeight = halfHeight * 2 * projection.VerticalScale * (ground ? projection.GroundDepthFactor : untextured ? projection.HeightFactor : 1),
            Color = color, CornerColors = corners, GroundAligned = ground, Additive = additive, SourceColorOnly = sourceColorOnly,
            Rotation = slope ?? rotation, Sheared = shear.HasValue, Untextured = untextured,
            PainterDepthKey = WorldPainterDepth.FromWorld(point) + height / IsometricProjection.StepWidth
        });
    }
}
