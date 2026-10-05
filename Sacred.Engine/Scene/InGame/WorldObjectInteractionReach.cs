using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Sacred.Granny.Meshes;
using Sacred.World.Geometry;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Interaction reach is measured from the authored ground footprint, not its blocked pivot.</summary>
internal static class WorldObjectInteractionReach
{
    public const float Distance = 2.0f;
    private static readonly ConditionalWeakTable<Mesh, GroundBounds> Bounds = new();

    public static Vector2 ClosestPoint(SceneModel model, Vector2 origin)
    {
        var bounds = Bounds.GetValue(model.Geometry.BindMesh, static mesh => new GroundBounds(mesh));
        Span<Vector2> corners = stackalloc Vector2[4];
        for (var i = 0; i < corners.Length; i++)
        {
            var local = new Vector3(i is 0 or 3 ? bounds.Minimum.X : bounds.Maximum.X,
                i < 2 ? bounds.Minimum.Y : bounds.Maximum.Y, 0);
            var ground = Vector3.Transform(local, model.Transform);
            corners[i] = IsometricProjection.IsoToWorld(new(ground.X, -ground.Y / MathF.Sqrt(2)));
        }
        var positive = false;
        var negative = false;
        var closest = new Vector2(model.Position.X, model.Position.Y);
        var bestDistance = float.PositiveInfinity;
        var area = 0.0f;
        for (var i = 0; i < corners.Length; i++)
        {
            var start = corners[i];
            var end = corners[(i + 1) % corners.Length];
            var edge = end - start;
            var offset = origin - start;
            var cross = edge.X * offset.Y - edge.Y * offset.X;
            positive |= cross > 0;
            negative |= cross < 0;
            var a = start - corners[0];
            var b = end - corners[0];
            area += a.X * b.Y - a.Y * b.X;
            var fraction = edge.LengthSquared() > 0 ? Math.Clamp(Vector2.Dot(offset, edge) / edge.LengthSquared(), 0, 1) : 0;
            var point = start + edge * fraction;
            var distance = Vector2.DistanceSquared(origin, point);
            if (distance < bestDistance) { closest = point; bestDistance = distance; }
        }
        return MathF.Abs(area) > 0.0001f && !(positive && negative) ? origin : closest;
    }

    public static bool Contains(SceneModel model, Vector2 origin) =>
        Vector2.DistanceSquared(origin, ClosestPoint(model, origin)) <= Distance * Distance;

    private sealed class GroundBounds
    {
        public Vector2 Minimum { get; } = Vector2.Zero;
        public Vector2 Maximum { get; } = Vector2.Zero;

        public GroundBounds(Mesh mesh)
        {
            if (mesh.Vertices.Length == 0) return;
            var minimum = new Vector2(float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity);
            foreach (var vertex in mesh.Vertices)
            {
                var point = new Vector2(vertex.Position.X, vertex.Position.Y);
                minimum = Vector2.Min(minimum, point);
                maximum = Vector2.Max(maximum, point);
            }
            Minimum = minimum; Maximum = maximum;
        }
    }
}
