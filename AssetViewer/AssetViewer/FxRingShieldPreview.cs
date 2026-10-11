using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Native circular strips, Gouraud colours, ground flare and marker planes in original draw order.</summary>
internal sealed class FxRingShieldPreview
{
    private readonly SacredRingShieldFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _draws;
    private readonly List<FxPreviewDraw> _visibleDraws = [];
    private readonly int _markerCapacity;
    public FxRingShieldPreview(SacredRingShieldFxDefinition definition, SacredParticleProjection projection)
    {
        _playback = new(definition); _projection = projection;
        _markerCapacity = definition.Magic is { } magic ? (int)magic.MaximumSegments : 1;
        var indices = new List<ushort>(); var draws = new List<FxPreviewDraw>();
        for (var marker = 0; marker < _markerCapacity; marker++)
        {
            draws.Add(new() { IndexStart = indices.Count, IndexCount = 6, Additive = true,
                SourceColorOnly = definition.Holy?.GroundSourceColorOnly ?? false,
                TextureName = definition.MarkerTexture ?? definition.SecondaryTexture, Mode = ParticleTextureMode.NativeFxSurfaceRgba });
            var v = checked((ushort)(marker * 4));
            indices.AddRange([v, (ushort)(v + 1), (ushort)(v + 2), (ushort)(v + 2), (ushort)(v + 1), (ushort)(v + 3)]);
        }
        var passes = definition.Holy is not null ? 1 : 2;
        for (var pass = 0; pass < passes; pass++)
        {
            draws.Add(new() { IndexStart = indices.Count, SourceColorOnly = true, Additive = true,
                TextureName = pass == 0 ? definition.SurfaceTexture : definition.SecondaryTexture, Mode = ParticleTextureMode.NativeFxSurfaceRgba });
            var start = _markerCapacity * 4 + pass * definition.RingVertices.Count;
            for (var vertex = 0; vertex < definition.RingVertices.Count - 2; vertex++)
            {
                var v = checked((ushort)(start + vertex));
                indices.AddRange(vertex % 2 == 0 ? new ushort[] { v, (ushort)(v + 1), (ushort)(v + 2) }
                    : new ushort[] { (ushort)(v + 1), v, (ushort)(v + 2) });
            }
        }
        Mesh = new(new VertexPositionNormalTexture[_markerCapacity * 4 + passes * definition.RingVertices.Count], indices.ToArray());
        _draws = draws.ToArray(); TextureNames = draws.Select(draw => draw.TextureName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Advance(2);
    }
    public Mesh Mesh { get; }
    public IReadOnlyList<FxPreviewDraw> Draws => _playback.Active ? _visibleDraws : [];
    public IReadOnlyList<string> TextureNames { get; }
    public bool Active => _playback.Active;
    public int Phase => _playback.Phase;
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            if (_playback.Definition.Magic is not { } p) yield break;
            var reach = _playback.Definition.Radius + _playback.Definition.MarkerVertices.Max(v => Math.Max(Math.Abs(v.Position.X), Math.Abs(v.Position.Y))) * (p.MarkerScaleOrigin + p.MarkerScaleWave);
            var height = _playback.Definition.RingVertices.Max(v => v.Position.Z) * (p.HeightScaleOrigin + p.HeightScaleWave + p.HitFlash);
            foreach (var x in new[] { -reach, reach })
            foreach (var y in new[] { -reach, reach })
            foreach (var z in new[] { 0, height }) yield return ToModel(new(x, y, z));
        }
    }
    public void Restart() { _playback.Restart(); Advance(0); }
    public void RequestHit() { _playback.RequestHit(); Advance(0); }
    public void Advance(float seconds)
    {
        _playback.Advance(seconds);
        var d = _playback.Definition;
        var markers = d.Holy is not null ? 1 : _playback.MarkerCount;
        _visibleDraws.Clear();
        for (var marker = 0; marker < markers; marker++)
        {
            _visibleDraws.Add(_draws[marker]);
            var matrix = d.Holy is not null ? Matrix4x4.Identity : _playback.MarkerMatrix(marker);
            for (var corner = 0; corner < 4; corner++)
            {
                var position = d.Holy is not null ? _playback.GroundVertex(corner)
                    : Vector3.Transform(d.MarkerVertices[corner].Position, matrix);
                var color = d.Holy is not null ? uint.MaxValue : d.MarkerVertices[corner].Color;
                var uv = d.Holy is not null ? SacredLayeredFlareFxPlayback.TextureCoordinate(corner) : d.MarkerVertices[corner].TextureCoordinate;
                Mesh.Vertices[marker * 4 + corner] = Vertex(position, color, uv);
            }
        }
        Array.Clear(Mesh.Vertices, markers * 4, (_markerCapacity - markers) * 4);
        for (var pass = 0; pass < (d.Holy is not null ? 1 : 2); pass++)
        {
            var matrix = _playback.RingMatrix(pass);
            for (var vertex = 0; vertex < d.RingVertices.Count; vertex++)
                Mesh.Vertices[_markerCapacity * 4 + pass * d.RingVertices.Count + vertex] = Vertex(
                    Vector3.Transform(d.RingVertices[vertex].Position, matrix), _playback.RingColor(vertex), d.RingVertices[vertex].TextureCoordinate);
            _draws[_markerCapacity + pass].IndexCount = Math.Max(0, _playback.RingVertexCount - 2) * 3;
            _visibleDraws.Add(_draws[_markerCapacity + pass]);
        }
        Mesh.NotifyVerticesChanged();
    }
    private VertexPositionNormalTexture Vertex(Vector3 position, uint color, Vector2 uv) =>
        new(ToModel(position), new(color & 0xFFFFFF, 0, (color >> 24) / 255f), uv);
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
