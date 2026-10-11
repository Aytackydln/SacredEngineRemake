using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Complete native ring strips, retaining individual rune resources and the leading ground flare.</summary>
internal sealed class FxRingSurfacePreview
{
    private readonly SacredBubbleTrapExplosionFxPlayback? _bubble;
    private readonly SacredTerrorSprinterFxPlayback? _terror;
    private readonly SacredParticleProjection _projection;
    private readonly FxPreviewDraw[] _draws;
    private readonly float _fitRadius;
    public FxRingSurfacePreview(SacredBubbleTrapExplosionFxDefinition definition, SacredParticleProjection projection)
    {
        _projection = projection; _bubble = new(definition);
        _fitRadius = definition.InitialState.InnerRadius + definition.Lifetime * definition.RadiusRate + definition.RingWidth;
        _draws = Enumerable.Range(0, definition.SegmentCount).Select(_ => Draw(definition.TextureName, definition.SourceColorOnly, definition.Additive)).ToArray();
        Mesh = CreateMesh(); TextureNames = [definition.TextureName]; Advance(.2f);
    }
    public FxRingSurfacePreview(SacredTerrorSprinterFxDefinition definition, SacredParticleProjection projection)
    {
        _projection = projection; _terror = new(definition);
        _fitRadius = Math.Max(definition.GroundFlare.HalfSize * MathF.Sqrt(2), definition.RingRadius + definition.RingHalfWidth);
        _draws = new[] { Draw(definition.GroundFlare.TextureName, definition.GroundFlare.SourceColorOnly, definition.GroundFlare.Additive) }
            .Concat(Enumerable.Range(0, definition.SegmentCount).Select(i => Draw(_terror.SegmentTexture(i), definition.RingSourceColorOnly, definition.RingAdditive))).ToArray();
        Mesh = CreateMesh(); TextureNames = new[] { definition.GroundFlare.TextureName }.Concat(definition.SegmentTextureVariants).ToArray();
        for (var step = 0; step < 60; step++) Advance(1f / 60);
    }
    public Mesh Mesh { get; }
    public bool Active => _bubble?.Active ?? _terror!.Active;
    public byte? Phase => _terror?.Phase;
    public IReadOnlyList<FxPreviewDraw> Draws => Active ? _draws : [];
    public IReadOnlyList<string> TextureNames { get; }
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            foreach (var x in new[] { -_fitRadius, _fitRadius })
                foreach (var y in new[] { -_fitRadius, _fitRadius })
                    foreach (var z in new[] { 0f, 0f }) yield return ToModel(new(x, y, z));
        }
    }
    public void Restart() { _bubble?.Restart(); _terror?.Restart(); Advance(0); }
    public void Advance(float seconds)
    {
        _bubble?.Advance(seconds); _terror?.Advance(seconds);
        if (!Active) { Array.Clear(Mesh.Vertices); Mesh.NotifyVerticesChanged(); return; }
        var start = 0;
        if (_terror is not null)
        {
            var ground = _terror.GroundSurface;
            for (var corner = 0; corner < 4; corner++) Mesh.Vertices[corner] = Vertex(
                Vector3.Transform(ground.Vertex(corner, Matrix4x4.Identity), _terror.GroundMatrix), ground.Layer.Color, ground.TextureCoordinate(corner));
            start = 4;
        }
        var ring = _bubble?.Surface() ?? _terror!.RingSurface();
        for (var segment = 0; segment < ring.SegmentCount; segment++)
            for (var corner = 0; corner < 4; corner++)
            {
                var v = ring.Vertex(segment, corner);
                Mesh.Vertices[start + segment * 4 + corner] = Vertex(Vector3.Transform(v.Position, ring.Matrix), v.Color, v.TextureCoordinate);
            }
        Mesh.NotifyVerticesChanged();
    }
    private Mesh CreateMesh()
    {
        var indices = new ushort[_draws.Length * 6];
        for (var i = 0; i < _draws.Length; i++)
        {
            _draws[i].IndexStart = i * 6;
            var v = (ushort)(i * 4);
            new ushort[] { v, (ushort)(v + 1), (ushort)(v + 2), (ushort)(v + 2), (ushort)(v + 1), (ushort)(v + 3) }.CopyTo(indices, i * 6);
        }
        return new(new VertexPositionNormalTexture[_draws.Length * 4], indices);
    }
    private static FxPreviewDraw Draw(string texture, bool source, bool additive) => new()
    { IndexCount = 6, TextureName = texture, SourceColorOnly = source, Additive = additive, Mode = ParticleTextureMode.NativeFxSurfaceRgba };
    private VertexPositionNormalTexture Vertex(Vector3 position, uint color, Vector2 uv) =>
        new(ToModel(position), new(color & 0xFFFFFF, 0, (color >> 24) / 255f), uv);
    private Vector3 ToModel(Vector3 native) => new(native.X * _projection.HorizontalScale,
        -native.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        native.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
