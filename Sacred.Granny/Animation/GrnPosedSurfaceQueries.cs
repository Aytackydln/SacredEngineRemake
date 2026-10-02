using System.Numerics;
using Sacred.Granny.Diagnostics;
using Sacred.Granny.Meshes;

namespace Sacred.Granny.Animation;

/// <summary>Pose-dependent material parameters, cached per surface without materializing the body mesh.</summary>
public sealed class GrnPosedSurfaceQueries(GrnAnimatedMesh animation)
{
    private sealed class Entry
    {
        public ulong Revision;
        public bool Valid;
        public Vector4 Parameters;
        public Vector2[] Positions = [];
        public readonly Dictionary<int, Vector2> UniquePositions = [];
    }
    private readonly Dictionary<(int Start, int Count), Entry> _surfaces = [];
    public bool TryGetRadialSweep(MeshSurface surface, out Vector4 parameters)
    {
        if (!animation.Pose.IsEvaluated) throw new InvalidOperationException("Evaluate a pose before querying a surface.");
        var key = (surface.IndexStart, surface.IndexCount);
        if (!_surfaces.TryGetValue(key, out var entry)) _surfaces.Add(key, entry = new());
        if (entry.Revision != animation.Pose.Revision)
        {
            using var measurement = AnimationPerformance.Measure(AnimationCpuStage.SurfaceQueries);
            var mesh = animation.BindMesh;
            var currentCpuMesh = animation.IsCpuMeshCurrent ? animation.Mesh : null;
            var start = Math.Max(0, surface.IndexStart);
            var end = (int)Math.Min(mesh.Indices.Length, (long)start + Math.Max(0, surface.IndexCount));
            if (entry.Positions.Length < Math.Max(0, end - start)) entry.Positions = new Vector2[end - start];
            var min = new Vector2(float.MaxValue); var max = new Vector2(float.MinValue);
            var count = 0;
            entry.UniquePositions.Clear();
            for (var i = start; i < end; i++)
            {
                var index = mesh.Indices[i];
                if (index >= mesh.Vertices.Length) continue;
                if (!entry.UniquePositions.TryGetValue(index, out var point))
                {
                    var position = currentCpuMesh is not null ? currentCpuMesh.Vertices[index].Position : animation.TransformPosition(index);
                    point = new Vector2(position.X, position.Z);
                    entry.UniquePositions.Add(index, point);
                }
                entry.Positions[count++] = point;
                min = Vector2.Min(min, point); max = Vector2.Max(max, point);
            }
            var center = (min + max) * .5f;
            var radius = 0f;
            for (var i = 0; i < count; i++) radius = Math.Max(radius, Vector2.Distance(center, entry.Positions[i]));
            entry.Valid = count > 0 && float.IsFinite(min.X) && float.IsFinite(min.Y) &&
                float.IsFinite(max.X) && float.IsFinite(max.Y) && float.IsFinite(radius) && radius > .0001f;
            entry.Parameters = entry.Valid ? new(center.X, center.Y, 1f / radius, 0) : default;
            entry.Revision = animation.Pose.Revision;
        }
        parameters = entry.Parameters;
        return entry.Valid;
    }
}
