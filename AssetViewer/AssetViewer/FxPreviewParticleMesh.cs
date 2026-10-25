using System;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Billboard geometry and native vertex colours for the shared world-particle preview.</summary>
internal static class FxPreviewParticleMesh
{
    public static (Mesh Mesh, FxPreviewDraw[] Slots) Create(int capacity)
    {
        if (capacity > ushort.MaxValue / 4) throw new NotSupportedException("FX exceeds the preview index capacity.");
        var indices = new ushort[capacity * 6];
        for (var index = 0; index < capacity; index++)
        {
            var vertex = index * 4; var start = index * 6;
            indices[start] = (ushort)vertex; indices[start + 1] = (ushort)(vertex + 1);
            indices[start + 2] = (ushort)(vertex + 2); indices[start + 3] = (ushort)(vertex + 2);
            indices[start + 4] = (ushort)(vertex + 1); indices[start + 5] = (ushort)(vertex + 3);
        }
        return (new(new VertexPositionNormalTexture[capacity * 4], indices),
            Enumerable.Range(0, capacity).Select(index => new FxPreviewDraw { IndexStart = index * 6, IndexCount = 6 }).ToArray());
    }

    public static Vector3[] NativeSeeds(Mesh mesh, SacredParticleProjection projection) =>
        mesh.Vertices.Select(vertex => new Vector3(vertex.Position.X / projection.HorizontalScale,
            -vertex.Position.Y / (MathF.Sqrt(2) * projection.GroundDepthFactor * projection.VerticalScale),
            vertex.Position.Z / (MathF.Sqrt(2) * projection.HeightFactor * projection.VerticalScale))).ToArray();

    public static void Write(WorldParticle particle, FxPreviewDraw draw, Mesh mesh, int index)
    {
        draw.TextureName = particle.Sprite.TextureName;
        draw.Additive = particle.Additive;
        draw.SourceColorOnly = particle.SourceColorOnly;
        draw.Untextured = particle.Untextured;
        var color = particle.Color;
        draw.UsesCornerTint = particle.CornerColors is not null;
        if (draw.UsesCornerTint) color = (color & 0xFF000000) | 0xFFFFFF;
        draw.Color = new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f,
            (color & 255) / 255f, (color >> 24) / 255f);
        var ground = IsometricProjection.WorldToModel(particle.WorldX, particle.WorldY);
        var center = new Vector3(ground, particle.Height * MathF.Sqrt(2));
        var halfWidth = particle.Size * .5f;
        var halfHeight = particle.RenderHeight * .5f;
        // Shared line angles are projected with screen Y down; the model billboard's up axis is Y up.
        var sine = MathF.Sin(particle.Rotation) * (particle.Untextured ? -1 : 1);
        var cosine = MathF.Cos(particle.Rotation);
        var side = particle.Sprite.AtlasColumns;
        var cell = particle.AtlasCell;
        for (var corner = 0; corner < 4; corner++)
        {
            var u = corner / 2; var v = corner % 2;
            var x = (u * 2 - 1) * halfWidth; var y = (v * 2 - 1) * halfHeight;
            var offset = particle.GroundAligned
                ? new Vector2(((u * 2 - 1) * cosine + (v * 2 - 1) * sine) * halfWidth,
                    ((u * 2 - 1) * sine - (v * 2 - 1) * cosine) * halfHeight)
                : new Vector2(x * cosine - y * sine, x * sine + y * cosine);
            var vertexColor = particle.CornerColors is { } colors ? colors[corner] : particle.Color;
            // Untextured quads have no atlas coordinates. Preserve their native vertex alpha in U.
            var uv = particle.Untextured ? new Vector2((vertexColor >> 24) / 255f, 0)
                : new Vector2((cell % side + u) / (float)side, (cell / side + 1 - v) / (float)particle.Sprite.AtlasRows);
            mesh.Vertices[index * 4 + corner] = new(center,
                new(offset.X, offset.Y, draw.UsesCornerTint ? (vertexColor & 0xFFFFFF) + 1 : 1), uv);
        }
    }
}
