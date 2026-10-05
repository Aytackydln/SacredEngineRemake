using System;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace Sacred.Inventory.Effects;

/// <summary>Updates retained native glow corners independently of their skeleton attachment.</summary>
internal sealed class NativeBillboardSizeAnimation(int firstVertex, SacredBillboardSizeAnimation definition)
{
    public void Update(Mesh mesh, float elapsedSeconds)
    {
        var weight = definition.AngularFrequency > 0
            ? (MathF.Sin(elapsedSeconds * definition.AngularFrequency) + 1) * 0.5f
            : Random.Shared.Next(32768) / 32767f;
        var size = definition.MinimumHalfSize +
            (definition.MaximumHalfSize - definition.MinimumHalfSize) * weight;
        for (var corner = 0; corner < 4; corner++)
        {
            var index = firstVertex + corner;
            var vertex = mesh.Vertices[index];
            mesh.Vertices[index] = vertex with { Normal = new Vector3(
                corner is 0 or 3 ? -size : size, corner < 2 ? -size : size, vertex.Normal.Z) };
        }
    }
}
