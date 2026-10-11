using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Adds complete native ring/ground submissions to the standard particle preview mesh.</summary>
internal sealed class FxExpandingBurstPreview
{
    private readonly SacredExpandingBurstFxPlayback _playback;
    private readonly SacredParticleProjection _projection;
    private readonly List<FxPreviewDraw> _draws = [];
    public FxExpandingBurstPreview(SacredExpandingBurstFxDefinition definition, SacredParticleProjection projection)
    { _playback = new(definition); _projection = projection; }
    public int Capacity => _playback.Definition.SegmentCount + 2;
    public IReadOnlyList<string> TextureNames => [_playback.Definition.RingTexture, _playback.Definition.GroundTexture];
    public IEnumerable<Vector3> CameraFitPoints
    {
        get
        {
            var d = _playback.Definition;
            var radius = Math.Max(d.GroundSizes.X * MathF.Sqrt(2), d.RingDuration * (d.EventRadiusRate ?? d.RadiusRate) + d.RingWidth);
            foreach (var x in new[] { -radius, radius })
                foreach (var y in new[] { -radius, radius }) yield return ToModel(new(x, y, 0));
        }
    }
    public void Restart() => _playback.Restart();
    public void Advance(float seconds) => _playback.Advance(seconds);
    public void Append(Mesh mesh, int firstSlot, List<FxPreviewDraw> draws)
    {
        _draws.Clear();
        if (!_playback.Active) { draws.Clear(); return; }
        _playback.BeginRender();
        var slot = firstSlot;
        if (_playback.RingVisible)
        {
            var ring = _playback.RingSurface();
            for (var segment = 0; segment < ring.SegmentCount; segment++, slot++)
            {
                for (var corner = 0; corner < 4; corner++)
                {
                    var vertex = ring.Vertex(segment, corner);
                    mesh.Vertices[slot * 4 + corner] = Vertex(Vector3.Transform(vertex.Position, ring.Matrix), vertex.Color, vertex.TextureCoordinate);
                }
                _draws.Add(Draw(slot, _playback.Definition.RingTexture));
            }
        }
        if (_playback.GroundVisible)
            for (var layer = 0; layer < 2; layer++, slot++)
            {
                var ground = _playback.GroundSurface(layer);
                for (var corner = 0; corner < 4; corner++)
                    mesh.Vertices[slot * 4 + corner] = Vertex(Vector3.Transform(ground.Vertex(corner, Matrix4x4.Identity), _playback.GroundMatrix(layer)),
                        ground.Layer.Color, ground.TextureCoordinate(corner));
                _draws.Add(Draw(slot, _playback.Definition.GroundTexture));
            }
        // Native rendering submits the ring and ground layers before the standard particles.
        draws.InsertRange(0, _draws);
    }
    private static FxPreviewDraw Draw(int slot, string texture) => new()
    { IndexStart = slot * 6, IndexCount = 6, TextureName = texture, SourceColorOnly = true, Additive = true, Mode = ParticleTextureMode.NativeFxSurfaceRgba };
    private VertexPositionNormalTexture Vertex(Vector3 p, uint color, Vector2 uv) => new(ToModel(p), new(color & 0xFFFFFF, 0, (color >> 24) / 255f), uv);
    private Vector3 ToModel(Vector3 p) => new(p.X * _projection.HorizontalScale,
        -p.Y * _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2),
        p.Z * _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2));
}
