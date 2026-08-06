using System.Numerics;

namespace Sacred.Granny;

/// <summary>Calculates a normalized radial coordinate for effects that travel from a model surface's center to its edge.</summary>
public static class MeshSurfaceRadialSweep
{
    public static bool TryCalculate(Mesh mesh, MeshSurface surface, out Vector4 parameters)
    {
        parameters = default;
        var indexStart = Math.Max(0, surface.IndexStart);
        var indexEnd = Math.Min(mesh.Indices.Length, indexStart + Math.Max(0, surface.IndexCount));
        if (indexStart >= indexEnd)
            return false;

        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        for (var index = indexStart; index < indexEnd; index++)
        {
            var vertexIndex = mesh.Indices[index];
            if (vertexIndex >= mesh.Vertices.Length)
                continue;

            var position = mesh.Vertices[vertexIndex].Position;
            var radialPosition = new Vector2(position.X, position.Z);
            min = Vector2.Min(min, radialPosition);
            max = Vector2.Max(max, radialPosition);
        }

        if (!float.IsFinite(min.X) || !float.IsFinite(min.Y) ||
            !float.IsFinite(max.X) || !float.IsFinite(max.Y))
            return false;

        var center = (min + max) * 0.5f;
        var radius = 0.0f;
        for (var index = indexStart; index < indexEnd; index++)
        {
            var vertexIndex = mesh.Indices[index];
            if (vertexIndex >= mesh.Vertices.Length)
                continue;

            var position = mesh.Vertices[vertexIndex].Position;
            radius = Math.Max(radius, Vector2.Distance(center, new Vector2(position.X, position.Z)));
        }

        if (!float.IsFinite(radius) || radius <= 0.0001f)
            return false;

        parameters = new Vector4(center.X, center.Y, 1.0f / radius, 0.0f);
        return true;
    }
}
