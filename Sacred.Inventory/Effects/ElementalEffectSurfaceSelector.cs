using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny;

namespace Sacred.Inventory.Effects;

internal static class ElementalEffectSurfaceSelector
{
    public static ElementalEffectSurface Select(
        IReadOnlyList<GrnSurfaceTriangleDiagnostics> surfaceTriangles,
        Vector3 start,
        Vector3 end)
    {
        var span = end - start;
        var length = span.Length();
        if (length < 0.001f)
            return ElementalEffectSurface.Empty;

        var axis = span / length;
        var selected = new List<ElementalEffectSurfaceTriangle>();
        Span<Vector3> polygon = stackalloc Vector3[5];
        foreach (var triangle in surfaceTriangles)
        {
            polygon[0] = triangle.A;
            polygon[1] = triangle.B;
            polygon[2] = triangle.C;
            var count = ClipAgainstAxialPlane(polygon, 3, start, axis, 0.0f, keepGreater: true);
            count = ClipAgainstAxialPlane(polygon, count, start, axis, length, keepGreater: false);
            for (var index = 1; index + 1 < count; index++)
                AddTriangle(selected, polygon[0], polygon[index], polygon[index + 1]);
        }

        return new ElementalEffectSurface(selected);
    }

    private static int ClipAgainstAxialPlane(
        Span<Vector3> polygon,
        int count,
        Vector3 origin,
        Vector3 axis,
        float planeDistance,
        bool keepGreater)
    {
        if (count == 0)
            return 0;

        Span<Vector3> output = stackalloc Vector3[5];
        var outputCount = 0;
        var previous = polygon[count - 1];
        var previousDistance = Vector3.Dot(previous - origin, axis) - planeDistance;
        var previousInside = keepGreater ? previousDistance >= 0.0f : previousDistance <= 0.0f;

        for (var index = 0; index < count; index++)
        {
            var current = polygon[index];
            var currentDistance = Vector3.Dot(current - origin, axis) - planeDistance;
            var currentInside = keepGreater ? currentDistance >= 0.0f : currentDistance <= 0.0f;
            if (currentInside != previousInside)
            {
                var amount = previousDistance / (previousDistance - currentDistance);
                output[outputCount++] = Vector3.Lerp(previous, current, amount);
            }
            if (currentInside)
                output[outputCount++] = current;

            previous = current;
            previousDistance = currentDistance;
            previousInside = currentInside;
        }

        output[..outputCount].CopyTo(polygon);
        return outputCount;
    }

    private static void AddTriangle(
        ICollection<ElementalEffectSurfaceTriangle> triangles,
        Vector3 a,
        Vector3 b,
        Vector3 c)
    {
        var cross = Vector3.Cross(b - a, c - a);
        var doubleArea = cross.Length();
        if (!float.IsFinite(doubleArea) || doubleArea <= 0.000001f)
            return;

        triangles.Add(new ElementalEffectSurfaceTriangle(a, b, c, doubleArea * 0.5f));
    }
}

internal sealed class ElementalEffectSurface
{
    private readonly IReadOnlyList<ElementalEffectSurfaceTriangle> _triangles;

    public ElementalEffectSurface(IReadOnlyList<ElementalEffectSurfaceTriangle> triangles)
    {
        _triangles = triangles;
        Area = 0.0f;
        foreach (var triangle in triangles)
            Area += triangle.Area;
    }

    public static ElementalEffectSurface Empty { get; } = new([]);
    public float Area { get; }
    public bool IsEmpty => _triangles.Count == 0 || Area <= 0.0f;

    public Vector3[] CreateUniformSamples(int count)
    {
        if (count <= 0 || IsEmpty)
            return [];

        var samples = new Vector3[count];
        var triangleIndex = 0;
        var areaBeforeTriangle = 0.0f;
        for (var sampleIndex = 0; sampleIndex < count; sampleIndex++)
        {
            var targetArea = Area * ((sampleIndex + 0.5f) / count);
            while (triangleIndex + 1 < _triangles.Count &&
                   areaBeforeTriangle + _triangles[triangleIndex].Area < targetArea)
            {
                areaBeforeTriangle += _triangles[triangleIndex].Area;
                triangleIndex++;
            }

            var triangle = _triangles[triangleIndex];
            var first = RadicalInverse((uint)sampleIndex + 1u);
            var second = Fractional((sampleIndex + 1) * 0.61803398875f);
            var root = MathF.Sqrt(first);
            samples[sampleIndex] = triangle.A * (1.0f - root) +
                                   triangle.B * (root * (1.0f - second)) +
                                   triangle.C * (root * second);
        }

        return samples;
    }

    private static float RadicalInverse(uint bits)
    {
        bits = (bits << 16) | (bits >> 16);
        bits = ((bits & 0x55555555u) << 1) | ((bits & 0xAAAAAAAAu) >> 1);
        bits = ((bits & 0x33333333u) << 2) | ((bits & 0xCCCCCCCCu) >> 2);
        bits = ((bits & 0x0F0F0F0Fu) << 4) | ((bits & 0xF0F0F0F0u) >> 4);
        bits = ((bits & 0x00FF00FFu) << 8) | ((bits & 0xFF00FF00u) >> 8);
        return bits * 2.3283064365386963e-10f;
    }

    private static float Fractional(float value) => value - MathF.Floor(value);
}

internal readonly record struct ElementalEffectSurfaceTriangle(
    Vector3 A,
    Vector3 B,
    Vector3 C,
    float Area);
