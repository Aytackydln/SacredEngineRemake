using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal sealed class FxControlledFlarePreview
{
    private readonly SacredControlledFlareFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _slots;
    private readonly List<FxPreviewDraw> _draws = [];
    public FxControlledFlarePreview(SacredControlledFlareFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        var count = definition.Kind == SacredControlledFlareKind.DeathCall ? (int)definition.InitialWeight + 1 : definition.Layers.Count;
        _slots = Enumerable.Range(0, count).Select(index => new FxPreviewDraw
            { IndexStart = index * 6, IndexCount = 6, Mode = ParticleTextureMode.NativeFxSurface }).ToArray();
        var indices = new List<ushort>();
        for (var index = 0; index < count; index++)
        {
            var v = checked((ushort)(index * 4));
            indices.AddRange([v, (ushort)(v + 1), (ushort)(v + 2), (ushort)(v + 2), (ushort)(v + 1), (ushort)(v + 3)]);
        }
        Mesh = new(new VertexPositionNormalTexture[count * 4], indices.ToArray());
        TextureNames = definition.Layers.Select(layer => layer.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Advance(.1f);
    }
    public Mesh Mesh { get; }
    public IReadOnlyList<string> TextureNames { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => Active ? _draws : [];
    public bool Active => _playback.Active;
    public void Restart() { _playback.Restart(); Advance(0); }
    public void RequestToggle() { _playback.RequestToggle(); Advance(0); }
    public void SetAtlasCell(int cell) { _playback.SetAtlasCell(cell); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds); _draws.Clear();
        var surfaces = _playback.Surfaces();
        for (var index = 0; index < surfaces.Count; index++)
        {
            var surface = surfaces[index]; var layer = surface.Layer; var draw = _slots[index];
            draw.TextureName = layer.TextureName; draw.Additive = layer.Additive; draw.SourceColorOnly = layer.SourceColorOnly;
            draw.Color = new((layer.Color >> 16 & 255) / 255f, (layer.Color >> 8 & 255) / 255f,
                (layer.Color & 255) / 255f, (layer.Color >> 24) / 255f); _draws.Add(draw);
            for (var corner = 0; corner < 4; corner++)
            {
                var point = surface.Vertex(corner);
                var position = new Vector3(point.X * _projection.HorizontalScale,
                    -point.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
                    point.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
                Mesh.Vertices[index * 4 + corner] = new(position, Vector3.One, surface.TextureCoordinate(corner));
            }
        }
        Mesh.NotifyVerticesChanged();
    }
}
