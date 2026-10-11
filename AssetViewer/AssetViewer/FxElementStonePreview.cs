using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Adapts the shared native model/surface geometry to the rotatable preview camera.</summary>
internal sealed class FxElementStonePreview
{
    private readonly SacredElementStoneFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly VertexPositionNormalTexture[] _modelVertices;
    private readonly Vector3 _modelOrigin;
    private readonly int _seed = Random.Shared.Next();
    private Random _random = null!;
    private int _randomValue;

    public FxElementStonePreview(SacredElementStoneFxDefinition definition, SacredParticleProjection projection, GrnAsset asset)
    {
        _playback = new(definition); _projection = projection;
        _modelVertices = asset.Mesh?.Vertices ?? throw new ArgumentException("Element Stone requires its authored GRN model.", nameof(asset));
        _modelOrigin = asset.Diagnostics?.SourceOriginOffset
            ?? throw new ArgumentException("Element Stone requires the authored GRN origin.", nameof(asset));
        var modelIndices = asset.Mesh!.Indices;
        if (_modelVertices.Length + 24 > ushort.MaxValue) throw new NotSupportedException("FX model exceeds preview index capacity.");
        var indices = new ushort[modelIndices.Length + 36];
        modelIndices.CopyTo(indices, 0);
        for (var quad = 0; quad < 6; quad++)
        {
            var start = modelIndices.Length + quad * 6; var vertex = _modelVertices.Length + quad * 4;
            indices[start] = (ushort)vertex; indices[start + 1] = (ushort)(vertex + 1);
            indices[start + 2] = (ushort)(vertex + 2); indices[start + 3] = (ushort)(vertex + 2);
            indices[start + 4] = (ushort)(vertex + 1); indices[start + 5] = (ushort)(vertex + 3);
        }
        Mesh = new(new VertexPositionNormalTexture[_modelVertices.Length + 24], indices);
        Draws = new FxPreviewDraw[7];
        Draws[0] = new() { IndexCount = modelIndices.Length, TextureName = definition.ModelTexture,
            Mode = ParticleTextureMode.NativeFxSurface, Additive = definition.ModelSourceColorOnly, SourceColorOnly = definition.ModelSourceColorOnly };
        for (var quad = 0; quad < 6; quad++)
            Draws[quad + 1] = new() { IndexStart = modelIndices.Length + quad * 6, IndexCount = 6,
                TextureName = quad < 3 ? definition.BeamTexture : quad == 3 ? definition.FlickerTexture
                    : quad == 4 ? definition.GlowTexture : definition.CoreTexture,
                Additive = true, SourceColorOnly = quad < 4 ? definition.SurfaceSourceColorOnly
                    : quad == 4 ? definition.GlowSourceColorOnly : definition.CoreSourceColorOnly,
                Mode = quad < 4 ? ParticleTextureMode.NativeFxSurface : ParticleTextureMode.NativeFx };
        TextureNames = Draws.Select(draw => draw.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Restart(); Advance(2);
    }

    public Mesh Mesh { get; }
    public FxPreviewDraw[] Draws { get; }
    public IReadOnlyList<string> TextureNames { get; }
    public bool IsBillboardVertex(int index) => index >= _modelVertices.Length + 16;
    public void Restart() { _playback.Restart(); _random = new(_seed); _randomValue = _random.Next(32768); Advance(0); }

    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        // Seeking zero refreshes geometry without consuming another native render random draw.
        if (seconds > 0) _randomValue = _random.Next(32768);
        var definition = _playback.Definition;
        for (var vertex = 0; vertex < _modelVertices.Length; vertex++)
        {
            var source = _modelVertices[vertex];
            // Restore the authored origin, then apply Gold getModel's Y reflection.
            var native = (source.Position + _modelOrigin) * new Vector3(1, -1, 1);
            Mesh.Vertices[vertex] = new(ToModel(_playback.ModelPosition(native)), Vector3.One, source.TexCoord);
        }
        for (var pass = 0; pass < 3; pass++)
        for (var vertex = 0; vertex < 4; vertex++)
        {
            var source = definition.BeamVertices[vertex];
            Mesh.Vertices[_modelVertices.Length + pass * 4 + vertex] = new(ToModel(_playback.BeamPosition(pass, vertex)),
                Rgb(source.Color), source.TextureCoordinate);
        }
        for (var vertex = 0; vertex < 4; vertex++)
        {
            var source = definition.FlickerVertices[vertex];
            Mesh.Vertices[_modelVertices.Length + 12 + vertex] = new(ToModel(source.Position),
                Rgb(vertex < 2 ? source.Color : definition.FlickerBottomColor),
                _playback.FlickerTextureCoordinate(vertex, _randomValue));
        }
        Flare(4, definition.GlowHeight, definition.GlowHalfSize, definition.GlowColor);
        Flare(5, definition.CoreHeight, _playback.CoreHalfSize(_randomValue), definition.CoreColor);
        Mesh.NotifyVerticesChanged();
    }

    private void Flare(int quad, float height, float size, uint color)
    {
        var draw = Draws[quad + 1];
        draw.Color = new(Rgb(color), (color >> 24) / 255f);
        for (var corner = 0; corner < 4; corner++)
        {
            var u = corner / 2; var v = corner % 2;
            Mesh.Vertices[_modelVertices.Length + quad * 4 + corner] = new(ToModel(new(0, 0, height)),
                new((u * 2 - 1) * size * _projection.HorizontalScale,
                    (v * 2 - 1) * size * _projection.VerticalScale, 1), new(u, 1 - v));
        }
    }

    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
    private static Vector3 Rgb(uint color) => new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f, (color & 255) / 255f);
}
