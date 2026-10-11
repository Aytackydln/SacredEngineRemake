using System.Numerics;
using Sacred.Particles.Particles;

namespace Sacred.Particles;

/// <summary>One native strip submission, including each repeated highlight pass.</summary>
public readonly record struct SacredWarCrySignFxStrip(float Angle, float Reach, uint Color)
{
    public Matrix4x4 Matrix => Matrix4x4.CreateRotationZ(-Angle);
    public SacredFxSurfaceVertexLayout Vertex(SacredWarCrySignFxDefinition definition, int index)
    {
        var vertex = definition.StripVertices[index];
        if (index is 0 or 1 or 3) vertex = vertex.WithPosition(vertex.Position with { Y = Reach });
        return vertex.WithColor(Color);
    }
}
