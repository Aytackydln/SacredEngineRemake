using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Authored GRN, two ground flares and the native 62-vertex cylindrical strip.</summary>
internal sealed class FxForgeElementPreview
{
    private readonly SacredForgeElementFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly VertexPositionNormalTexture[] _model;
    private readonly Vector3 _origin;
    private readonly FxPreviewDraw[] _draws;

    public FxForgeElementPreview(SacredForgeElementFxDefinition definition, SacredParticleProjection projection, GrnAsset asset)
    {
        _playback = new(definition); _projection = projection;
        _model = asset.Mesh?.Vertices ?? throw new ArgumentException("Forge Element requires its authored GRN.", nameof(asset));
        _origin = asset.Diagnostics?.SourceOriginOffset ?? throw new ArgumentException("Forge Element requires the authored origin.", nameof(asset));
        if (_model.Length + 8 + definition.RingVertices.Count > ushort.MaxValue) throw new NotSupportedException("FX exceeds preview index capacity.");
        var indices = new List<ushort>(asset.Mesh!.Indices);
        for (var pass = 0; pass < 2; pass++)
        {
            var v = _model.Length + pass * 4;
            indices.AddRange([(ushort)v, (ushort)(v + 1), (ushort)(v + 2), (ushort)(v + 2), (ushort)(v + 1), (ushort)(v + 3)]);
        }
        var ringStart = indices.Count;
        for (var triangle = 0; triangle < definition.RingVertices.Count - 2; triangle++)
        {
            var v = _model.Length + 8 + triangle;
            indices.AddRange([(ushort)(v + triangle % 2), (ushort)(v + 1 - triangle % 2), (ushort)(v + 2)]);
        }
        Mesh = new(new VertexPositionNormalTexture[_model.Length + 8 + definition.RingVertices.Count], indices.ToArray());
        _draws = [
            Draw(definition.RotatingFlareTexture, asset.Mesh.Indices.Length, 6),
            Draw(definition.GroundFlareTexture, asset.Mesh.Indices.Length + 6, 6),
            Draw(definition.ModelTexture, 0, asset.Mesh.Indices.Length),
            Draw(definition.RingTexture, ringStart, indices.Count - ringStart)];
        _draws[3].Color = new(1, 1, 1, 0); // Native trail RGB has zero alpha; ONE source preserves it.
        _draws[2].Additive = definition.ModelSourceColorOnly;
        _draws[2].SourceColorOnly = definition.ModelSourceColorOnly;
        TextureNames = _draws.Select(draw => draw.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Advance(2);
    }

    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _playback.Visible ? _draws : Array.Empty<FxPreviewDraw>();
    public IReadOnlyList<string> TextureNames { get; }
    public byte Phase => _playback.State.Phase;
    public void Restart() { _playback.Restart(); Advance(0); }
    public void RequestRelease() => _playback.RequestRelease();

    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        for (var index = 0; index < _model.Length; index++)
        {
            var source = _model[index];
            var native = (source.Position + _origin) * new Vector3(1, -1, 1);
            Mesh.Vertices[index] = new(ToModel(_playback.ModelPosition(native)), Vector3.One, source.TexCoord);
        }
        for (var pass = 0; pass < 2; pass++)
        for (var corner = 0; corner < 4; corner++)
            Mesh.Vertices[_model.Length + pass * 4 + corner] = new(ToModel(_playback.FlarePosition(pass, corner)),
                Vector3.One, new(corner < 2 ? 1 : 0, corner % 2));
        for (var index = 0; index < _playback.Definition.RingVertices.Count; index++)
        {
            var color = _playback.RingColor(index);
            Mesh.Vertices[_model.Length + 8 + index] = new(ToModel(_playback.RingPosition(index)),
                new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f),
                _playback.Definition.RingVertices[index].TextureCoordinate);
        }
        Mesh.NotifyVerticesChanged();
    }

    private static FxPreviewDraw Draw(string texture, int start, int count) => new()
    {
        TextureName = texture, IndexStart = start, IndexCount = count, Additive = true,
        SourceColorOnly = true, Mode = ParticleTextureMode.NativeFxSurface
    };
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
