using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Native ground planes retain their orientation, UVs, full RGBA and authored layer order.</summary>
internal sealed class FxLayeredFlarePreview
{
    private readonly SacredLayeredFlareFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly IReadOnlyList<FxPreviewDraw> _draws;
    public FxLayeredFlarePreview(SacredLayeredFlareFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        var indices = new List<ushort>();
        var draws = new List<FxPreviewDraw>();
        for (var layer = 0; layer < definition.Layers.Count; layer++)
        {
            var source = definition.Layers[layer]; var v = checked((ushort)(layer * 4));
            draws.Add(new() { IndexStart = indices.Count, IndexCount = 6, TextureName = source.TextureName,
                Additive = source.Additive, SourceColorOnly = source.SourceColorOnly, Mode = ParticleTextureMode.NativeFxSurface,
                Color = new((source.Color >> 16 & 255) / 255f, (source.Color >> 8 & 255) / 255f,
                    (source.Color & 255) / 255f, (source.Color >> 24) / 255f) });
            indices.AddRange([v, (ushort)(v + 1), (ushort)(v + 2), (ushort)(v + 2), (ushort)(v + 1), (ushort)(v + 3)]);
        }
        Mesh = new(new VertexPositionNormalTexture[definition.Layers.Count * 4], indices.ToArray());
        _draws = draws.ToArray(); TextureNames = draws.Select(draw => draw.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Advance(2);
    }
    public Mesh Mesh { get; }
    // The native manager retires the object through its active-byte getter.
    public IReadOnlyList<FxPreviewDraw> Draws => Active ? _draws : [];
    public IReadOnlyList<string> TextureNames { get; }
    public bool Active => _playback.Active;
    public void Restart() { _playback.Restart(); Advance(0); }
    public void ExtendDuration(uint units) => _playback.ExtendDuration(units);
    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        for (var layer = 0; layer < _draws.Count; layer++)
        for (var corner = 0; corner < 4; corner++)
        {
            var native = _playback.LayerVertex(layer, corner);
            var position = new Vector3(native.X * _projection.HorizontalScale,
                -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
                native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
            Mesh.Vertices[layer * 4 + corner] = new(position, Vector3.One, SacredLayeredFlareFxPlayback.TextureCoordinate(corner));
        }
        Mesh.NotifyVerticesChanged();
    }
}
