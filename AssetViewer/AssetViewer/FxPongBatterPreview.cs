using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Native triangle-list paddle and repeated camera-facing edge sprites.</summary>
internal sealed class FxPongBatterPreview
{
    private readonly SacredPongBatterFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly int _surfaceCount;
    private readonly FxPreviewDraw[] _draws;

    public FxPongBatterPreview(SacredPongBatterFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        _surfaceCount = definition.SurfaceVertices.Count;
        var capacity = Enumerable.Range(0, definition.Edges.Count).Sum(_playback.EdgeSpriteCount);
        if (_surfaceCount + capacity * 4 > ushort.MaxValue) throw new NotSupportedException("Pong preview exceeds index capacity.");
        var indices = new List<ushort>();
        for (var vertex = 0; vertex < _surfaceCount; vertex++) indices.Add((ushort)vertex);
        for (var sprite = 0; sprite < capacity; sprite++)
        {
            var v = _surfaceCount + sprite * 4;
            indices.AddRange([(ushort)v, (ushort)(v + 1), (ushort)(v + 2),
                (ushort)(v + 2), (ushort)(v + 1), (ushort)(v + 3)]);
        }
        Mesh = new(new VertexPositionNormalTexture[_surfaceCount + capacity * 4], indices.ToArray());
        _draws = [new() { TextureName = definition.SurfaceTexture, IndexCount = _surfaceCount,
            Additive = true, SourceColorOnly = true, Mode = ParticleTextureMode.NativeFxSurface },
            new() { TextureName = definition.EdgeTexture, IndexStart = _surfaceCount,
                Additive = true, SourceColorOnly = false, Mode = ParticleTextureMode.NativeFx, Color = Rgba(definition.EdgeColor) }];
        TextureNames = _draws.Select(draw => draw.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Advance(2);
    }

    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _draws;
    public IReadOnlyList<string> TextureNames { get; }
    public bool IsBillboardVertex(int index) => index >= _surfaceCount;
    public void Restart() { _playback.Restart(); Advance(0); }
    public void RequestHit() { _playback.RequestHit(); Advance(0); }

    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        var d = _playback.Definition;
        var color = Rgba(_playback.SurfaceColor);
        for (var vertex = 0; vertex < _surfaceCount; vertex++)
        {
            var source = d.SurfaceVertices[vertex];
            Mesh.Vertices[vertex] = new(ToModel(source.Position), new(color.X, color.Y, color.Z),
                _playback.SurfaceTextureCoordinate(source.TextureCoordinate));
        }
        var total = 0;
        var halfSize = _playback.EdgeHalfSize;
        for (var edge = 0; edge < d.Edges.Count; edge++)
        for (var sprite = 0; sprite < _playback.EdgeSpriteCount(edge); sprite++)
        {
            var position = ToModel(_playback.EdgeSpritePosition(edge, sprite));
            for (var corner = 0; corner < 4; corner++)
            {
                var u = corner / 2; var v = corner % 2;
                Mesh.Vertices[_surfaceCount + total * 4 + corner] = new(position,
                    new((u * 2 - 1) * halfSize * _projection.HorizontalScale,
                        (v * 2 - 1) * halfSize * _projection.VerticalScale, 1), new(u, 1 - v));
            }
            total++;
        }
        Array.Clear(Mesh.Vertices, _surfaceCount + total * 4, Mesh.Vertices.Length - _surfaceCount - total * 4);
        _draws[1].IndexCount = total * 6;
        Mesh.NotifyVerticesChanged();
    }

    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
    private static Vector4 Rgba(uint color) => new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f,
        (color & 255) / 255f, (color >> 24) / 255f);
}
