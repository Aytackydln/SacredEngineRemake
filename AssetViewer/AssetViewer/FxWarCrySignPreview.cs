using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Complete swept strips and ground glow in original submission order, including overdraw.</summary>
internal sealed class FxWarCrySignPreview
{
    private readonly SacredWarCrySignFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _draws;
    private int _count;
    public FxWarCrySignPreview(SacredWarCrySignFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        var capacity = definition.SectorCount * 2 * (1 + definition.HighlightPassCount);
        var vertices = definition.StripVertices.Count;
        var indices = new List<ushort> { 0, 1, 2, 2, 1, 3 };
        var ground = definition.GroundFlare;
        var draws = new List<FxPreviewDraw> { new()
        {
            IndexCount = 6, TextureName = ground.TextureName, Additive = ground.Additive,
            SourceColorOnly = ground.SourceColorOnly, Mode = ParticleTextureMode.NativeFxSurfaceRgba
        } };
        for (var strip = 0; strip < capacity; strip++)
        {
            draws.Add(new() { IndexStart = indices.Count, IndexCount = (vertices - 2) * 3,
                TextureName = definition.StripTexture, Additive = true, Mode = ParticleTextureMode.NativeFxSurfaceRgba });
            var start = 4 + strip * vertices;
            for (var vertex = 0; vertex < vertices - 2; vertex++)
            {
                var v = checked((ushort)(start + vertex));
                indices.AddRange(vertex % 2 == 0 ? new ushort[] { v, (ushort)(v + 1), (ushort)(v + 2) }
                    : new ushort[] { (ushort)(v + 1), v, (ushort)(v + 2) });
            }
        }
        _draws = draws.ToArray();
        Mesh = new(new VertexPositionNormalTexture[4 + capacity * vertices], indices.ToArray());
        TextureNames = draws.Select(d => d.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for (var step = 0; step < 150; step++) Advance(1f / 60);
    }
    public Mesh Mesh { get; }
    public bool Active => _playback.Active;
    public byte Phase => _playback.Phase;
    public IReadOnlyList<FxPreviewDraw> Draws => Active ? _draws.Take(_count).ToArray() : [];
    public IReadOnlyList<string> TextureNames { get; }
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            var d = _playback.Definition;
            var reach = Math.Max(Math.Abs(d.MinimumReach), Math.Abs(d.MaximumReach));
            var width = d.StripVertices.Max(v => Math.Abs(v.Position.X));
            var radius = Math.Max(MathF.Sqrt(reach * reach + width * width), d.GroundFlare.HalfSize * MathF.Sqrt(2));
            var height = d.StripVertices.Max(v => Math.Abs(v.Position.Z));
            foreach (var x in new[] { -radius, radius })
            foreach (var y in new[] { -radius, radius })
            foreach (var z in new[] { -height, height }) yield return ToModel(new(x, y, z));
        }
    }
    public void Restart() { _playback.Restart(); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        var ground = _playback.GroundSurface;
        for (var corner = 0; corner < 4; corner++)
            Mesh.Vertices[corner] = Vertex(ground.Vertex(corner, Matrix4x4.Identity), ground.Layer.Color, ground.TextureCoordinate(corner));
        var strips = _playback.Strips(); _count = strips.Count + 1;
        var d = _playback.Definition;
        for (var strip = 0; strip < strips.Count; strip++)
        for (var vertex = 0; vertex < d.StripVertices.Count; vertex++)
        {
            var v = strips[strip].Vertex(d, vertex);
            Mesh.Vertices[4 + strip * d.StripVertices.Count + vertex] = Vertex(
                Vector3.Transform(v.Position, strips[strip].Matrix), v.Color, v.TextureCoordinate);
        }
        var used = 4 + strips.Count * d.StripVertices.Count;
        Array.Clear(Mesh.Vertices, used, Mesh.Vertices.Length - used);
        Mesh.NotifyVerticesChanged();
    }
    private VertexPositionNormalTexture Vertex(Vector3 position, uint color, Vector2 uv) =>
        new(ToModel(position), new(color & 0xFFFFFF, 0, (color >> 24) / 255f), uv);
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
