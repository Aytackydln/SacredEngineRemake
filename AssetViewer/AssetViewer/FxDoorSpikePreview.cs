using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>All six authored Door Spike models with native transforms and ordinary lit material.</summary>
internal sealed class FxDoorSpikePreview
{
    private SacredDoorSpikeFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly GrnAsset _asset;
    private readonly FxPreviewDraw[] _slots;
    private readonly List<FxPreviewDraw> _draws = [];

    public FxDoorSpikePreview(SacredDoorSpikeFxDefinition definition, SacredParticleProjection projection, GrnAsset asset)
    {
        if (asset.Mesh is null || asset.Diagnostics is null)
            throw new ArgumentException("Door Spike requires its authored model and origin.", nameof(asset));
        _playback = new(definition); _projection = projection; _asset = asset;
        var mesh = asset.Mesh;
        var vertexCount = checked(mesh.Vertices.Length * definition.DrawCount);
        if (vertexCount > ushort.MaxValue) throw new NotSupportedException("Door Spike exceeds preview index capacity.");
        var indices = Enumerable.Range(0, definition.DrawCount).SelectMany(slot =>
            mesh.Indices.Select(index => checked((ushort)(index + slot * mesh.Vertices.Length)))).ToArray();
        Mesh = new(new VertexPositionNormalTexture[vertexCount], indices);
        _slots = Enumerable.Range(0, definition.DrawCount).Select(slot => new FxPreviewDraw
        {
            IndexStart = slot * mesh.Indices.Length, IndexCount = mesh.Indices.Length,
            TextureName = definition.SurfaceTexture, Mode = ParticleTextureMode.Luminance, UsesModelLighting = true
        }).ToArray();
        TextureNames = [definition.SurfaceTexture];
        var fit = new SacredDoorSpikeFxPlayback(definition);
        var full = fit.CaptureState(); full.Envelope = 1; fit.RestoreState(in full);
        CameraFitPoints = Enumerable.Range(0, definition.DrawCount).SelectMany(slot => mesh.Vertices.Select(vertex =>
            ToModel(Vector3.Transform(NativePosition(vertex), fit.ModelMatrix(slot))))
            .Append(ToModel(full.Offsets[slot]))).ToArray();
        Advance(.5f);
    }

    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _draws;
    public IReadOnlyList<string> TextureNames { get; }
    public IReadOnlyList<Vector3> CameraFitPoints { get; }
    public bool Active => _playback.Active;
    public byte Phase => _playback.Phase;
    public void RequestRelease() => _playback.RequestRelease();
    public void Restart()
    {
        // A viewer restart creates a fresh object; native reinitialization retains elapsed time and flags.
        _playback = new(_playback.Definition);
        Advance(0);
    }

    public void Advance(float seconds)
    {
        _playback.Advance(seconds); _draws.Clear();
        for (var slot = 0; slot < _slots.Length; slot++)
        {
            var matrix = _playback.ModelMatrix(slot);
            var invertible = Matrix4x4.Invert(matrix, out var inverse);
            var normals = Matrix4x4.Transpose(inverse);
            for (var index = 0; index < _asset.Mesh!.Vertices.Length; index++)
            {
                var source = _asset.Mesh.Vertices[index];
                var position = ToModel(Vector3.Transform(NativePosition(source), matrix));
                var normal = invertible ? Vector3.TransformNormal(source.Normal * new Vector3(1, -1, 1), normals) : Vector3.UnitZ;
                normal = new(normal.X / _projection.HorizontalScale,
                    -normal.Y / (_projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2)),
                    normal.Z / (_projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2)));
                Mesh.Vertices[slot * _asset.Mesh.Vertices.Length + index] = new(position, Vector3.Normalize(normal), source.TexCoord);
            }
        }
        if (_playback.Active) _draws.AddRange(_slots);
        Mesh.NotifyVerticesChanged();
    }
    private Vector3 NativePosition(VertexPositionNormalTexture vertex) =>
        (vertex.Position + _asset.Diagnostics!.SourceOriginOffset) * new Vector3(1, -1, 1);
    private Vector3 ToModel(Vector3 point) => new(point.X * _projection.HorizontalScale,
        -point.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        point.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
