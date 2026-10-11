using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

internal sealed class FxSpriteFlarePreview
{
    private readonly SacredSpriteFlareFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _draws;
    public FxSpriteFlarePreview(SacredSpriteFlareFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        var count = definition.Layers.Count + (definition.GroundFlare is null ? 0 : 1);
        var indices = Enumerable.Range(0, count).SelectMany(i => new ushort[]
            { (ushort)(i * 4), (ushort)(i * 4 + 1), (ushort)(i * 4 + 2), (ushort)(i * 4 + 2), (ushort)(i * 4 + 1), (ushort)(i * 4 + 3) }).ToArray();
        Mesh = new(new VertexPositionNormalTexture[count * 4], indices);
        _draws = Enumerable.Range(0, count).Select(i => new FxPreviewDraw { IndexStart = i * 6, IndexCount = 6 }).ToArray();
        TextureNames = _playback.Surfaces().Select(s => s.Layer.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Advance(.1f);
    }
    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _playback.Active ? _draws : [];
    public IReadOnlyList<string> TextureNames { get; }
    public bool Active => _playback.Active;
    public bool IsBillboardVertex(int index) => index < _playback.Definition.Layers.Count * 4;
    public void RequestRemoval() { _playback.RequestRemoval(); Advance(0); }
    public void Restart() { _playback.Restart(); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        var surfaces = _playback.Surfaces();
        for (var index = 0; index < surfaces.Count; index++)
        {
            var surface = surfaces[index]; var layer = surface.Layer; var draw = _draws[index];
            draw.TextureName = layer.TextureName; draw.Additive = layer.Additive; draw.SourceColorOnly = layer.SourceColorOnly;
            draw.Color = Rgba(layer.Color); draw.Mode = surface.Billboard ? ParticleTextureMode.NativeFx : ParticleTextureMode.NativeFxSurface;
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
