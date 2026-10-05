using System.Numerics;
using System.Runtime.CompilerServices;
using Sacred.Granny.Meshes;

namespace Sacred.Engine.Scene.InGame;

/// <summary>Projects authored model bounds, matching Sacred's rectangular mouse-selection path.</summary>
internal static class WorldModelFocusPicker
{
    private static readonly ConditionalWeakTable<Mesh, Bounds> MeshBounds = new();

    public static bool TryScore(SceneModel model, SacredCamera camera, Vector2 mouse,
        int width, int height, out float score)
    {
        score = float.PositiveInfinity;
        if (width <= 0 || height <= 0 || mouse.X < 0 || mouse.Y < 0 || mouse.X >= width || mouse.Y >= height)
            return false;
        var bounds = MeshBounds.GetValue(model.Geometry.BindMesh, static mesh => new Bounds(mesh));
        var transform = model.Transform * camera.View * camera.Projection;
        var minimum = new Vector2(float.PositiveInfinity);
        var maximum = new Vector2(float.NegativeInfinity);
        for (var corner = 0; corner < 8; corner++)
        {
            var vertex = new Vector3((corner & 1) == 0 ? bounds.Minimum.X : bounds.Maximum.X,
                (corner & 2) == 0 ? bounds.Minimum.Y : bounds.Maximum.Y,
                (corner & 4) == 0 ? bounds.Minimum.Z : bounds.Maximum.Z);
            var clip = Vector4.Transform(new Vector4(vertex, 1), transform);
            if (clip.W <= 0) return false;
            var screen = new Vector2((clip.X / clip.W + 1) * width * 0.5f,
                (1 - clip.Y / clip.W) * height * 0.5f);
            minimum = Vector2.Min(minimum, screen);
            maximum = Vector2.Max(maximum, screen);
        }
        if (mouse.X < minimum.X || mouse.X > maximum.X || mouse.Y < minimum.Y || mouse.Y > maximum.Y)
            return false;
        score = Vector2.DistanceSquared(mouse, (minimum + maximum) * 0.5f);
        return true;
    }

    private sealed class Bounds
    {
        public Vector3 Minimum { get; }
        public Vector3 Maximum { get; }
        public Bounds(Mesh mesh)
        {
            var minimum = new Vector3(float.PositiveInfinity);
            var maximum = new Vector3(float.NegativeInfinity);
            foreach (var vertex in mesh.Vertices)
            {
                minimum = Vector3.Min(minimum, vertex.Position);
                maximum = Vector3.Max(maximum, vertex.Position);
            }
            Minimum = minimum; Maximum = maximum;
        }
    }
}
