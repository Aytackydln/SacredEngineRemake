using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Authored vertical strip with Gouraud RGBA, plus the native camera-facing glow.</summary>
internal sealed class FxStrikeSymbolPreview
{
    private readonly SacredStrikeSymbolFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _draws;
    private Random _random = new(1);
    public FxStrikeSymbolPreview(SacredStrikeSymbolFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        Mesh = new(new VertexPositionNormalTexture[8], [0, 1, 2, 2, 1, 3, 4, 5, 6, 6, 5, 7]);
        _draws = [new() { IndexCount = 6, Additive = true, Mode = ParticleTextureMode.NativeFxSurfaceRgba },
            new() { IndexStart = 6, IndexCount = 6, TextureName = definition.FlareTexture, Additive = true,
                SourceColorOnly = definition.FlareSourceColorOnly, Color = Rgba(definition.FlareColor) }];
        TextureNames = definition.Textures.Append(definition.FlareTexture).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Advance(.1f);
    }
    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _playback.Active ? _draws : [];
    public IReadOnlyList<string> TextureNames { get; }
    public int TextureFrame => _playback.TextureFrame;
    public bool Active => _playback.Active;
    public bool IsBillboardVertex(int index) => index >= 4;
    public void Restart() { _playback.Restart(); _random = new(1); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds, () => _random.Next(32768));
        var d = _playback.Definition;
        _draws[0].TextureName = d.Textures[_playback.TextureFrame];
        _draws[1].Color = Rgba(_playback.FlareColor);
        for (var corner = 0; corner < 4; corner++)
        {
            var source = d.SurfaceVertices[corner];
            var color = _playback.SurfaceColor(corner);
            Mesh.Vertices[corner] = new(ToModel(source.Position), new(color & 0xFFFFFF, 0, (color >> 24) / 255f),
                _playback.SurfaceTextureCoordinate(corner));
            var local = _playback.FlareVertex(corner);
            Mesh.Vertices[4 + corner] = new(ToModel(d.FlareOffset),
                new(local.X * _projection.HorizontalScale, local.Y * _projection.VerticalScale, 1),
                SacredStrikeSymbolFxPlayback.FlareTextureCoordinate(corner));
        }
        Mesh.NotifyVerticesChanged();
    }
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
    private static Vector4 Rgba(uint color) => new((color >> 16 & 255) / 255f, (color >> 8 & 255) / 255f,
        (color & 255) / 255f, (color >> 24) / 255f);
}
