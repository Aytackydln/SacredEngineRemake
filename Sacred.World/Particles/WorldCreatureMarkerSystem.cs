using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

public sealed record WorldCreatureMarker(uint Reference, Vector2 Position, float Height, float Radius,
    float HealthFraction, uint HealthColor, uint BorderColor, bool ShowHealth, bool Champion, bool Elite);

/// <summary>Creature ground symbols participate in world painter ordering, underneath the actor mesh.</summary>
public sealed class WorldCreatureMarkerSystem
{
    public IReadOnlyList<WorldCreatureMarker> Markers { get; set; } = [];
    private double _seconds;

    public void Advance(float seconds, SacredParticleProjection projection, List<WorldParticle> output)
    {
        if (!float.IsFinite(seconds) || seconds < 0) throw new ArgumentOutOfRangeException(nameof(seconds));
        _seconds += seconds;
        var definition = SacredCreatureMarkerDefinition.Original;
        // Gold rotates the decorations using the performance clock's low 16 milliseconds.
        var rotation = (float)((long)(_seconds * 1000) & 0xFFFF) * definition.RotationPerMillisecond;
        foreach (var marker in Markers)
        {
            if (marker.Radius <= 0 || !float.IsFinite(marker.Radius)) continue;
            if (marker.ShowHealth)
            {
                Add(definition.HealthTexture, marker.Radius, marker.HealthColor, 0, true, marker.HealthFraction);
                Add(definition.BorderTexture, marker.Radius, marker.BorderColor, 1);
            }
            if (marker.Champion) Add(definition.ChampionTexture, marker.Radius * definition.SpecialRadiusScale, 0xFFFF0000, 2, rotation: -rotation);
            if (marker.Elite) Add(definition.EliteTexture, marker.Radius * definition.SpecialRadiusScale, 0xFFFF0000, 3, rotation: -rotation);

            void Add(string texture, float radius, uint color, int order, bool arc = false, float rotation = 0)
            {
                var height = marker.Height * projection.HeightFactor * projection.VerticalScale;
                output.Add(new(unchecked((int)marker.Reference), new(texture, 1, 1, 1, 1, ParticleShaderKind.ItemParticle),
                    marker.Position.X, marker.Position.Y, height, radius * 2 * projection.HorizontalScale,
                    (color >> 24) / 255f, order)
                {
                    RenderHeight = radius * 2 * projection.VerticalScale * projection.GroundDepthFactor,
                    Color = color, GroundAligned = true, HealthArc = arc, Rotation = rotation,
                    UiBrightness = true, GroundPlaneDepth = true,
                    GroundDecal = true,
                    // The ground plane must be behind its own mesh even when their anchors coincide.
                    PainterDepthKey = WorldPainterDepth.FromWorld(marker.Position) + height / IsometricProjection.StepWidth - .001f
                });
            }
        }
    }
    public void Clear() { Markers = []; _seconds = 0; }
}
