using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal sealed class FxPulseFlarePreview
{
    private readonly SacredPulseFlareFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _draws;
    private readonly bool[] _billboards;
    private int _count;
    public FxPulseFlarePreview(SacredPulseFlareFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        var count = definition.Kind == SacredPulseFlareKind.Transdim ? definition.PlaneCount : definition.Layers.Count;
        _billboards = new bool[count];
        var indices = Enumerable.Range(0, count).SelectMany(i => new ushort[]
            { (ushort)(i * 4), (ushort)(i * 4 + 1), (ushort)(i * 4 + 2), (ushort)(i * 4 + 2), (ushort)(i * 4 + 1), (ushort)(i * 4 + 3) }).ToArray();
        Mesh = new(new VertexPositionNormalTexture[count * 4], indices);
        _draws = Enumerable.Range(0, count).Select(i => new FxPreviewDraw { IndexStart = i * 6, IndexCount = 6 }).ToArray();
        TextureNames = definition.Layers.Select(s => s.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (definition.Kind == SacredPulseFlareKind.Zhadum) _playback.RequestHit();
        Advance(.1f);
    }
    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _playback.Active ? _draws.Take(_count).ToArray() : [];
    public IReadOnlyList<string> TextureNames { get; }
    public bool Active => _playback.Active;
    public bool IsBillboardVertex(int index) => _billboards[index / 4];
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            var d = _playback.Definition; var ground = d.Layers.Max(l => l.HalfSize) * MathF.Sqrt(2);
            var x = ground; var y = ground;
            if (d.Kind == SacredPulseFlareKind.Transdim)
            { y += d.PositionBias; x = y + d.ResetInterval * d.ResetInterval * d.SpreadScale; }
            var a = ToModel(new(-x, -y, 0)); var b = ToModel(new(x, y, 0));
            var low = Vector3.Min(a, b); var high = Vector3.Max(a, b);
            if (d.Kind != SacredPulseFlareKind.Transdim)
            {
                var layer = d.Layers[^1]; var center = ToModel(layer.Offset);
                var radius = d.Kind == SacredPulseFlareKind.PongBall ? d.RadiusBias + d.RadiusAmplitude : layer.HalfSize;
                var extent = new Vector3(radius * MathF.Sqrt(_projection.HorizontalScale * _projection.HorizontalScale + _projection.VerticalScale * _projection.VerticalScale));
                low = Vector3.Min(low, center - extent); high = Vector3.Max(high, center + extent);
            }
            foreach (var px in new[] { low.X, high.X })
            foreach (var py in new[] { low.Y, high.Y })
            foreach (var pz in new[] { low.Z, high.Z }) yield return new(px, py, pz);
        }
    }
    public void RequestHit() { _playback.RequestHit(); Advance(0); }
    public void Restart() { _playback.Restart(); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        var surfaces = _playback.Surfaces(); _count = surfaces.Count;
        Array.Clear(_billboards);
        Array.Clear(Mesh.Vertices, _count * 4, Mesh.Vertices.Length - _count * 4);
        for (var index = 0; index < surfaces.Count; index++)
        {
            _billboards[index] = surfaces[index].Billboard;
            var surface = surfaces[index]; var layer = surface.Layer; var draw = _draws[index];
            draw.TextureName = layer.TextureName; draw.Additive = layer.Additive; draw.SourceColorOnly = layer.SourceColorOnly;
            draw.Color = Rgba(layer.Color); draw.Mode = surface.Billboard ? ParticleTextureMode.NativeFx : ParticleTextureMode.NativeFxSurface;
            // NativeFxSurface consumes Normal.xyz as vertex RGB tint, not a geometric normal.
            for (var corner = 0; corner < 4; corner++)
            {
                var vertex = surface.Vertex(corner, Matrix4x4.Identity);
                Mesh.Vertices[index * 4 + corner] = surface.Billboard
                    ? new(ToModel(layer.Offset), new((vertex.X - layer.Offset.X) * _projection.HorizontalScale,
                        (vertex.Y - layer.Offset.Y) * _projection.VerticalScale, 1), surface.TextureCoordinate(corner))
                    : new(ToModel(vertex), Vector3.One, surface.TextureCoordinate(corner));
            }
        }
        Mesh.NotifyVerticesChanged();
    }
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
    private static Vector4 Rgba(uint color) => new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f,
        (color & 255) / 255f, (color >> 24) / 255f);
}
