using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;
using Sacred.Particles.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Two authored model passes, twenty fixed-view ribbons and a billboard glow.</summary>
internal sealed class FxUnderworldGlyphPreview
{
    private readonly SacredUnderworldGlyphFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly VertexPositionNormalTexture[] _model;
    private readonly Vector3 _origin;
    private readonly int _seed = Random.Shared.Next();
    private readonly int _ribbonStart;
    private readonly int _glowStart;

    public FxUnderworldGlyphPreview(SacredUnderworldGlyphFxDefinition definition, SacredParticleProjection projection, GrnAsset asset)
    {
        _projection = projection;
        _model = asset.Mesh?.Vertices ?? throw new ArgumentException("Glyph requires its authored GRN.", nameof(asset));
        _origin = asset.Diagnostics?.SourceOriginOffset ?? throw new ArgumentException("Glyph requires its authored origin.", nameof(asset));
        _ribbonStart = _model.Length * 2;
        _glowStart = _ribbonStart + SacredUnderworldGlyphFxStateLayout.WaveCount * definition.RibbonVertices.Count;
        if (_glowStart + 4 > ushort.MaxValue) throw new NotSupportedException("Glyph exceeds preview index capacity.");
        var indices = new List<ushort>();
        var draws = new List<FxPreviewDraw>();
        for (var pass = 0; pass < 2; pass++)
        {
            draws.Add(Draw(definition.ModelTexture, indices.Count, asset.Mesh!.Indices.Length, definition.ModelSourceColorOnly));
            draws[^1].Color = Rgba(definition.ModelColor);
            indices.AddRange(asset.Mesh!.Indices.Select(index => (ushort)(index + pass * _model.Length)));
        }
        for (var wave = 0; wave < SacredUnderworldGlyphFxStateLayout.WaveCount; wave++)
        {
            var count = (definition.RibbonVertices.Count - 2) * 3;
            draws.Add(Draw(definition.RibbonTexture, indices.Count, count, definition.RibbonSourceColorOnly));
            for (var triangle = 0; triangle < definition.RibbonVertices.Count - 2; triangle++)
            {
                var v = _ribbonStart + wave * definition.RibbonVertices.Count + triangle;
                indices.AddRange([(ushort)(v + triangle % 2), (ushort)(v + 1 - triangle % 2), (ushort)(v + 2)]);
            }
        }
        draws.Add(new() { TextureName = definition.GlowTexture, IndexStart = indices.Count, IndexCount = 6,
            Mode = ParticleTextureMode.NativeFx, Additive = true, SourceColorOnly = definition.GlowSourceColorOnly,
            Color = Rgba(definition.GlowColor) });
        indices.AddRange([(ushort)_glowStart, (ushort)(_glowStart + 1), (ushort)(_glowStart + 2),
            (ushort)(_glowStart + 2), (ushort)(_glowStart + 1), (ushort)(_glowStart + 3)]);
        Draws = draws.ToArray();
        TextureNames = Draws.Select(draw => draw.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Mesh = new(new VertexPositionNormalTexture[_glowStart + 4], indices.ToArray());
        var random = new Random(_seed);
        _playback = new(definition, () => random.Next(32768));
        Advance(2);
    }

    public Mesh Mesh { get; }
    public FxPreviewDraw[] Draws { get; }
    public IReadOnlyList<string> TextureNames { get; }
    public bool IsBillboardVertex(int index) => index >= _glowStart;
    public void Restart()
    {
        var random = new Random(_seed);
        _playback.Restart(() => random.Next(32768));
        Advance(0);
    }

    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        for (var pass = 0; pass < 2; pass++)
        for (var index = 0; index < _model.Length; index++)
        {
            var source = _model[index];
            var native = (source.Position + _origin) * new Vector3(1, -1, 1);
            Mesh.Vertices[pass * _model.Length + index] = new(ToModel(_playback.ModelPosition(native)), Vector3.One,
                _playback.ModelTextureCoordinate(pass, source.TexCoord));
        }
        var definition = _playback.Definition;
        for (var wave = 0; wave < SacredUnderworldGlyphFxStateLayout.WaveCount; wave++)
        for (var index = 0; index < definition.RibbonVertices.Count; index++)
        {
            var source = _playback.RibbonVertex(wave, index);
            Mesh.Vertices[_ribbonStart + wave * definition.RibbonVertices.Count + index] = new(
                ToModel(source.Position + new Vector3(0, 0, definition.RibbonHeight)), Rgb(source.Color), source.TextureCoordinate);
        }
        for (var corner = 0; corner < 4; corner++)
        {
            var u = corner / 2; var v = corner % 2;
            Mesh.Vertices[_glowStart + corner] = new(ToModel(new(0, 0, definition.GlowHeight)),
                new((u * 2 - 1) * definition.GlowHalfSize * _projection.HorizontalScale,
                    (v * 2 - 1) * definition.GlowHalfSize * _projection.VerticalScale, 1), new(u, 1 - v));
        }
        Mesh.NotifyVerticesChanged();
    }

    private static FxPreviewDraw Draw(string texture, int start, int count, bool sourceOnly) => new()
    { TextureName = texture, IndexStart = start, IndexCount = count, Mode = ParticleTextureMode.NativeFxSurface,
        SourceColorOnly = sourceOnly, Additive = sourceOnly };
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
    private static Vector3 Rgb(uint color) => new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f);
    private static Vector4 Rgba(uint color) => new(Rgb(color), (color >> 24) / 255f);
}
