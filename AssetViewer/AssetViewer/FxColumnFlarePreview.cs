using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal sealed class FxColumnFlarePreview
{
    private readonly SacredColumnFlareFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _draws;
    public FxColumnFlarePreview(SacredColumnFlareFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        var count = definition.SliceCount + 1;
        var indices = Enumerable.Range(0, count).SelectMany(i => new ushort[]
            { (ushort)(i * 4), (ushort)(i * 4 + 1), (ushort)(i * 4 + 2), (ushort)(i * 4 + 2), (ushort)(i * 4 + 1), (ushort)(i * 4 + 3) }).ToArray();
        Mesh = new(new VertexPositionNormalTexture[count * 4], indices);
        _draws = Enumerable.Range(0, count).Select(i => new FxPreviewDraw
        {
            IndexStart = i * 6, IndexCount = 6, TextureName = definition.Layer.TextureName,
            Mode = ParticleTextureMode.NativeFxSurface, Additive = definition.Layer.Additive, SourceColorOnly = definition.Layer.SourceColorOnly
        }).ToArray();
        TextureNames = [definition.Layer.TextureName]; Advance(2.1f);
    }
    public Mesh Mesh { get; }
    public IReadOnlyList<string> TextureNames { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => Active ? _draws : [];
    public bool Active => _playback.Active;
    public byte Phase => _playback.Phase;
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            var d = _playback.Definition; var size = d.Layer.HalfSize;
            var height = (d.SliceCount - 1) * d.HeightScale * (d.HeightSineBias + 1);
            foreach (var x in new[] { -size, size })
            foreach (var y in new[] { -size, size })
            foreach (var z in new[] { 0f, height }) yield return ToModel(new Vector3(x, y, z) + d.Layer.Offset);
        }
    }
    public void Restart() { _playback.Restart(); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds); var surfaces = _playback.Surfaces();
        for (var index = 0; index < surfaces.Count; index++)
        {
            var surface = surfaces[index]; var color = surface.Layer.Color;
            _draws[index].Color = new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f,
                (color & 255) / 255f, (color >> 24) / 255f);
            for (var corner = 0; corner < 4; corner++)
                Mesh.Vertices[index * 4 + corner] = new(ToModel(surface.Vertex(corner)), Vector3.One, surface.TextureCoordinate(corner));
        }
        Mesh.NotifyVerticesChanged();
    }
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
