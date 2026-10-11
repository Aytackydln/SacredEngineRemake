using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Assets;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Authored randomized spike models, ordinary material and optional additive glow.</summary>
internal sealed class FxAnducarPreview
{
    private readonly SacredAnducarFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly GrnAsset[] _models;
    private readonly int[] _starts;
    private readonly FxPreviewDraw[][] _slots;
    private readonly List<FxPreviewDraw> _draws = [];
    private readonly int _seed = Random.Shared.Next();
    private readonly Vector3 _endpointDifference;

    public FxAnducarPreview(SacredAnducarFxDefinition definition, SacredParticleProjection projection,
        IReadOnlyList<GrnAsset> models, float distance)
    {
        _projection = projection; _models = models.ToArray(); _endpointDifference = new(distance, 0, 0);
        if (_models.Length != definition.ModelNames.Count || _models.Any(asset => asset.Mesh is null || asset.Diagnostics is null))
            throw new ArgumentException("Anducar requires all authored models and origins.", nameof(models));
        var random = new Random(_seed);
        _playback = new(definition, () => random.Next(32768), _endpointDifference);
        _starts = new int[definition.Count]; _slots = new FxPreviewDraw[definition.Count][];
        var indices = new List<ushort>(); var vertices = 0;
        for (var slot = 0; slot < definition.Count; slot++)
        {
            var mesh = _models[_playback.ModelSelector(slot)].Mesh!;
            _starts[slot] = vertices;
            vertices += mesh.Vertices.Length;
            if (vertices > ushort.MaxValue) throw new NotSupportedException("Anducar exceeds preview index capacity.");
            var body = new FxPreviewDraw { TextureName = definition.SurfaceTexture, IndexStart = indices.Count,
                IndexCount = mesh.Indices.Length, Mode = (ParticleTextureMode)1, UsesModelLighting = true };
            indices.AddRange(mesh.Indices.Select(index => checked((ushort)(index + _starts[slot]))));
            _slots[slot] = definition.HasGlowPass ? [body, new() { TextureName = definition.GlowTexture,
                IndexStart = body.IndexStart, IndexCount = body.IndexCount, Additive = true,
                SourceColorOnly = definition.GlowSourceColorOnly, Mode = ParticleTextureMode.NativeFxSurface,
                Color = new((definition.GlowColor >> 16 & 255) / 255f, (definition.GlowColor >> 8 & 255) / 255f,
                    (definition.GlowColor & 255) / 255f, (definition.GlowColor >> 24) / 255f) }] : [body];
        }
        // Glow needs vertex RGB instead of normals; use a second copy of each model.
        if (definition.HasGlowPass)
        {
            if (vertices * 2 > ushort.MaxValue) throw new NotSupportedException("Anducar glow exceeds preview index capacity.");
            foreach (var slot in _slots)
            {
                slot[1].IndexStart = indices.Count;
                indices.AddRange(indices.Skip(slot[0].IndexStart).Take(slot[0].IndexCount)
                    .Select(index => checked((ushort)(index + vertices))).ToArray());
            }
        }
        Mesh = new(new VertexPositionNormalTexture[vertices * (definition.HasGlowPass ? 2 : 1)], indices.ToArray());
        TextureNames = _slots.SelectMany(slot => slot).Select(draw => draw.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var fit = new List<Vector3>();
        var state = _playback.CaptureState();
        for (var slot = 0; slot < state.Count; slot++)
        {
            fit.Add(ToModel(state.Offsets[slot]));
            var scale = state.Scales[slot];
            var matrix = Matrix4x4.CreateScale(scale * definition.HorizontalScaleFactor, scale * definition.HorizontalScaleFactor, scale) *
                Matrix4x4.CreateRotationZ(-state.Angles[slot]) * Matrix4x4.CreateTranslation(state.Offsets[slot]);
            var asset = _models[state.ModelSelectors[slot]];
            fit.AddRange(asset.Mesh!.Vertices.Select(vertex => ToModel(Vector3.Transform(
                (vertex.Position + asset.Diagnostics!.SourceOriginOffset) * new Vector3(1, -1, 1), matrix))));
        }
        CameraFitPoints = fit.ToArray();
        for (var step = 0; step < 42; step++) Advance(1f / 60);
    }

    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _draws;
    public IReadOnlyList<string> TextureNames { get; }
    public IReadOnlyList<Vector3> CameraFitPoints { get; }
    public void Restart()
    {
        var random = new Random(_seed);
        _playback.Restart(() => random.Next(32768), _endpointDifference);
        Advance(0);
    }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds); _draws.Clear();
        var glowOffset = Mesh.Vertices.Length / 2;
        for (var slot = 0; slot < _starts.Length; slot++)
        {
            var asset = _models[_playback.ModelSelector(slot)];
            var matrix = _playback.ModelMatrix(slot);
            var invertible = Matrix4x4.Invert(matrix, out var inverse);
            var normalMatrix = Matrix4x4.Transpose(inverse);
            for (var index = 0; index < asset.Mesh!.Vertices.Length; index++)
            {
                var source = asset.Mesh.Vertices[index];
                var native = (source.Position + asset.Diagnostics!.SourceOriginOffset) * new Vector3(1, -1, 1);
                var position = ToModel(Vector3.Transform(native, matrix));
                var normal = invertible ? Vector3.TransformNormal(source.Normal * new Vector3(1, -1, 1), normalMatrix) : Vector3.UnitZ;
                normal = new(normal.X / _projection.HorizontalScale,
                    -normal.Y / (_projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2)),
                    normal.Z / (_projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2)));
                var vertex = _starts[slot] + index;
                Mesh.Vertices[vertex] = new(position, Vector3.Normalize(normal), source.TexCoord);
                if (_slots[slot].Length == 2) Mesh.Vertices[vertex + glowOffset] = new(position, Vector3.One, source.TexCoord);
            }
            if (_playback.DrawsModel(slot)) _draws.AddRange(_slots[slot]);
        }
        Mesh.NotifyVerticesChanged();
    }
    private Vector3 ToModel(Vector3 point) => new(point.X * _projection.HorizontalScale,
        -point.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        point.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
