using System.Numerics;
using Sacred.Particles;
using Sacred.World.Geometry;

namespace Sacred.World.Particles;

/// <summary>Persistent world-object glints selected by Items.pak, independent of replicated FX objects.</summary>
public sealed class WorldObjectSparkleSystem
{
    private readonly List<WorldObjectSparkle> _objects = [];
    private double _elapsedSeconds;
    public int ActiveCount => _objects.Count;

    // Replaced every frame by the presentation, so pickup, despawn and reference reuse
    // cannot leave a sparkle attached to an old item instance.
    public void SetObjects(IEnumerable<WorldObjectSparkle> objects)
    {
        var previousCount = _objects.Count;
        _objects.Clear();
        _objects.AddRange(objects);
        if (previousCount != _objects.Count)
            Console.WriteLine($"World item sparkles active: {_objects.Count}.");
    }

    public void Clear() { _objects.Clear(); _elapsedSeconds = 0; }

    internal void Update(float seconds, SacredParticleQuality quality, SacredParticleProjection projection,
        List<WorldParticle> output)
    {
        _elapsedSeconds += seconds;
        foreach (var obj in _objects)
        for (var index = 0; index < SacredObjectSparkles.Count(quality); index++)
        {
            var (offset, innerHalfSize, outerColor) = SacredObjectSparkles.Evaluate(obj.Reference,
                (ulong)(_elapsedSeconds * 1000), index);
            var ground = IsometricProjection.IsoToWorld(projection.Project(new(offset.X, offset.Y, 0)));
            var point = obj.Position + ground;
            var height = (obj.Height + offset.Z) * projection.HeightFactor * projection.VerticalScale;
            Append(SacredObjectSparkles.OuterTexture, SacredObjectSparkles.OuterHalfSize, outerColor, index * 2);
            Append(SacredObjectSparkles.InnerTexture, innerHalfSize, 0xffbbbbbb, index * 2 + 1);

            void Append(string texture, float halfSize, uint color, int order)
            {
                output.Add(new(unchecked((int)obj.Reference), new(texture, 1, 1, 1, 1, ParticleShaderKind.ItemParticle),
                    point.X, point.Y, height, 2 * halfSize * projection.HorizontalScale, 1, order)
                {
                    Color = color, Additive = true, SourceColorOnly = true,
                    RenderHeight = 2 * halfSize * projection.VerticalScale,
                    PainterDepthKey = WorldPainterDepth.FromWorld(point) + height / IsometricProjection.StepWidth
                });
            }
        }
    }
}

/// <param name="Height">Native model units, before particle-camera projection.</param>
public readonly record struct WorldObjectSparkle(uint Reference, Vector2 Position, float Height);
