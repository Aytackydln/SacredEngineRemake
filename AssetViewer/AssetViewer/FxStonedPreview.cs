using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>The authored Stoned model with the material resolved from its Items.pak row.</summary>
internal sealed class FxStonedPreview
{
    private SacredStonedFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly GrnAsset _asset;
    private readonly uint _seed = (uint)Random.Shared.Next();
    private readonly FxPreviewDraw _draw;
    private readonly List<FxPreviewDraw> _draws = [];

    public FxStonedPreview(SacredStonedFxDefinition definition, SacredParticleProjection projection,
        GrnAsset asset, string materialTexture)
    {
        if (asset.Mesh is null || asset.Diagnostics is null)
            throw new ArgumentException("Stoned requires its authored model and origin.", nameof(asset));
        _playback = new(definition, _seed); _projection = projection; _asset = asset;
        Mesh = new(new VertexPositionNormalTexture[asset.Mesh.Vertices.Length], asset.Mesh.Indices.ToArray());
        _draw = new() { IndexCount = asset.Mesh.Indices.Length, TextureName = materialTexture,
            Mode = ParticleTextureMode.Luminance, UsesModelLighting = true };
        TextureNames = [materialTexture];
        Advance(0);
        CameraFitPoints = Mesh.Vertices.Select(v => v.Position).ToArray();
    }
    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _draws;
    public IReadOnlyList<string> TextureNames { get; }
    public IReadOnlyList<Vector3> CameraFitPoints { get; }
    public bool Active => _playback.Active;
    public float ElapsedTime => _playback.CaptureState().ElapsedTime;
    public void Restart() { _playback = new(_playback.Definition, _seed); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds); _draws.Clear();
        var matrix = _playback.ModelMatrix();
        Matrix4x4.Invert(matrix, out var inverse);
        var normals = Matrix4x4.Transpose(inverse);
        for (var index = 0; index < _asset.Mesh!.Vertices.Length; index++)
        {
            var source = _asset.Mesh.Vertices[index];
            var native = (source.Position + _asset.Diagnostics!.SourceOriginOffset) * new Vector3(1, -1, 1);
            var position = ToModel(Vector3.Transform(native, matrix));
            var normal = Vector3.TransformNormal(source.Normal * new Vector3(1, -1, 1), normals);
            normal = new(normal.X / _projection.HorizontalScale,
                -normal.Y / (_projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2)),
                normal.Z / (_projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2)));
            Mesh.Vertices[index] = new(position, Vector3.Normalize(normal), source.TexCoord);
        }
        if (_playback.Active) _draws.Add(_draw);
        Mesh.NotifyVerticesChanged();
    }
    private Vector3 ToModel(Vector3 point) => new(point.X * _projection.HorizontalScale,
        -point.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        point.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
