using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Complete native ground planes and both crossed helical strips, in native draw order.</summary>
internal sealed class FxConfusionPreview
{
    private readonly SacredConfusionFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _draws;
    private Random _random = new(1);
    public FxConfusionPreview(SacredConfusionFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        var indices = new List<ushort>(); var draws = new List<FxPreviewDraw>();
        for (var layer = 0; layer < definition.GroundLayers.Count; layer++)
        {
            var l = definition.GroundLayers[layer];
            draws.Add(new() { IndexStart = indices.Count, IndexCount = 6, Additive = l.Additive,
                SourceColorOnly = l.SourceColorOnly, TextureName = l.TextureName, Mode = ParticleTextureMode.NativeFxSurfaceRgba });
            var v = checked((ushort)(layer * 4));
            indices.AddRange([v, (ushort)(v + 1), (ushort)(v + 2), (ushort)(v + 2), (ushort)(v + 1), (ushort)(v + 3)]);
        }
        var count = definition.RibbonVertices.Count / 2;
        for (var strip = 0; strip < 2; strip++)
        {
            draws.Add(new() { IndexStart = indices.Count, IndexCount = (count - 2) * 3,
                Additive = true, TextureName = definition.RibbonTexture, Mode = ParticleTextureMode.NativeFxSurfaceRgba });
            var start = definition.GroundLayers.Count * 4 + strip * count;
            for (var vertex = 0; vertex < count - 2; vertex++)
            {
                var v = checked((ushort)(start + vertex));
                indices.AddRange(vertex % 2 == 0 ? new ushort[] { v, (ushort)(v + 1), (ushort)(v + 2) }
                    : new ushort[] { (ushort)(v + 1), v, (ushort)(v + 2) });
            }
        }
        _draws = draws.ToArray();
        Mesh = new(new VertexPositionNormalTexture[definition.GroundLayers.Count * 4 + definition.RibbonVertices.Count], indices.ToArray());
        TextureNames = draws.Select(d => d.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        for (var step = 0; step < 120; step++) Advance(1f / 60);
    }
    public Mesh Mesh { get; }
    public bool Active => _playback.Active;
    public IReadOnlyList<FxPreviewDraw> Draws => Active ? _draws : [];
    public IReadOnlyList<string> TextureNames { get; }
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            var radius = _playback.Definition.GroundLayers.Max(l => l.HalfSize) * MathF.Sqrt(2);
            foreach (var x in new[] { -radius, radius })
            foreach (var y in new[] { -radius, radius })
            foreach (var z in new[] { 0, _playback.Definition.MaximumGroundHeight }) yield return ToModel(new(x, y, z));
        }
    }
    public void Restart() { _playback.Restart(); _random = new(1); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds, () => _random.Next(32768));
        var d = _playback.Definition;
        for (var layer = 0; layer < d.GroundLayers.Count; layer++)
        for (var corner = 0; corner < 4; corner++)
            Mesh.Vertices[layer * 4 + corner] = Vertex(_playback.GroundVertex(layer, corner),
                d.GroundLayers[layer].Color, SacredLayeredFlareFxPlayback.TextureCoordinate(corner));
        var count = d.RibbonVertices.Count / 2;
        for (var strip = 0; strip < 2; strip++)
        for (var vertex = 0; vertex < count; vertex++)
        {
            var v = _playback.RibbonVertex(strip, vertex);
            Mesh.Vertices[d.GroundLayers.Count * 4 + strip * count + vertex] = Vertex(v.Position, v.Color, v.TextureCoordinate);
        }
        Mesh.NotifyVerticesChanged();
    }
    private VertexPositionNormalTexture Vertex(Vector3 position, uint color, Vector2 uv) =>
        new(ToModel(position), new(color & 0xFFFFFF, 0, (color >> 24) / 255f), uv);
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
