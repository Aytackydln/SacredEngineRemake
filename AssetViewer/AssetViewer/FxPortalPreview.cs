using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Sacred.Core.World.Portals;
using Sacred.Granny.Meshes;
using Sacred.Particles;

namespace AssetViewer.AssetViewer;

/// <summary>Rotatable native stargate mesh; shared native colour animation supplies Gouraud vertex colours.</summary>
internal sealed class FxPortalPreview
{
    private const int Columns = 20;
    private const int Rows = 34;
    private const int SurfaceVertices = Columns * Rows;
    private const int SurfaceIndices = (Columns - 1) * (Rows - 1) * 6;
    private readonly SacredParticleProjection _projection;
    private readonly IReadOnlyList<string> _surfaceTextures;
    private readonly int _seed = Random.Shared.Next();
    private Random _random = null!;
    private float _seconds;
    private int _cycle;

    public FxPortalPreview(SacredPortalVariant variant, SacredParticleProjection projection)
    {
        _projection = projection;
        _surfaceTextures = SacredPortalAppearance.GetSurfaceTextures(variant);
        var indices = new ushort[SurfaceIndices + 12];
        var offset = 0;
        for (var row = 0; row < Rows - 1; row++)
        for (var column = 0; column < Columns - 1; column++)
        {
            var vertex = row * Columns + column;
            indices[offset++] = (ushort)vertex;
            indices[offset++] = (ushort)(vertex + 1);
            indices[offset++] = (ushort)(vertex + Columns);
            indices[offset++] = (ushort)(vertex + Columns + 1);
            indices[offset++] = (ushort)(vertex + Columns);
            indices[offset++] = (ushort)(vertex + 1);
        }
        for (var whirl = 0; whirl < 2; whirl++)
        {
            var vertex = SurfaceVertices + whirl * 4;
            indices[offset++] = (ushort)vertex; indices[offset++] = (ushort)(vertex + 1);
            indices[offset++] = (ushort)(vertex + 2); indices[offset++] = (ushort)vertex;
            indices[offset++] = (ushort)(vertex + 2); indices[offset++] = (ushort)(vertex + 3);
        }
        Mesh = new(new VertexPositionNormalTexture[SurfaceVertices + 8], indices);
        Draws = [new() { IndexCount = SurfaceIndices, Mode = ParticleTextureMode.NativePortalSurface },
            new() { IndexStart = SurfaceIndices, IndexCount = 6,
                TextureName = SacredPortalAppearance.GetFirstWhirlTexture(variant),
                Color = new(1, 1, 1, SacredPortalAppearance.FirstWhirlOpacity) },
            new() { IndexStart = SurfaceIndices + 6, IndexCount = 6, Additive = true,
                TextureName = SacredPortalAppearance.GetSecondWhirlTexture(variant),
                Color = new(1, 1, 1, SacredPortalAppearance.SecondWhirlOpacity) }];
        TextureNames = _surfaceTextures.Concat(Draws.Skip(1).Select(draw => draw.TextureName))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Restart();
        Advance(2);
    }

    public Mesh Mesh { get; }
    public FxPreviewDraw[] Draws { get; }
    public IReadOnlyList<string> TextureNames { get; }

    public void Restart()
    {
        _seconds = 0; _cycle = 0; _random = new(_seed);
        Draws[0].TextureName = NextTexture();
        Draws[0].OverlayTextureName = NextTexture();
        Advance(0);
    }

    public void Advance(float seconds)
    {
        _seconds += seconds;
        while (_cycle < (int)(_seconds / 5))
        {
            _cycle++;
            Draws[0].TextureName = Draws[0].OverlayTextureName!;
            Draws[0].OverlayTextureName = NextTexture();
        }
        var heightScale = _projection.HeightFactor * _projection.VerticalScale * MathF.Sqrt(2);
        var depthScale = _projection.GroundDepthFactor * _projection.VerticalScale * MathF.Sqrt(2);
        for (var row = 0; row < Rows; row++)
        for (var column = 0; column < Columns; column++)
        {
            var grid = new Vector2(column - 9.5f, row - 17);
            var wave = MathF.Cos(2 * grid.Length() - 5 * _seconds) * 5;
            var uv = new Vector2(column / 19f, 1 - row / 33f);
            Mesh.Vertices[row * Columns + column] = new(
                new((column - 10) * 6 * _projection.HorizontalScale, -wave * depthScale,
                    ((row - 17) * 6 + SacredPortalAppearance.CenterHeight) * heightScale),
                SacredPortalSurfaceAnimation.SampleColour(uv, _seconds), new(column / 33f, uv.Y));
        }
        for (var whirl = 0; whirl < 2; whirl++)
        for (var corner = 0; corner < 4; corner++)
        {
            var angle = MathF.PI / 4 + corner * MathF.PI / 2 + _seconds * (whirl == 0 ? .3f : .4f);
            Mesh.Vertices[SurfaceVertices + whirl * 4 + corner] = new(
                new(MathF.Sin(angle) * SacredPortalAppearance.WhirlHalfWidth * _projection.HorizontalScale, 0,
                    (MathF.Cos(angle) * SacredPortalAppearance.WhirlHalfHeight + SacredPortalAppearance.CenterHeight) * heightScale),
                Vector3.Zero, corner switch { 0 => new(1, 0), 1 => Vector2.One, 2 => new(0, 1), _ => Vector2.Zero });
        }
        Mesh.NotifyVerticesChanged();
    }

    private string NextTexture() => _surfaceTextures[_random.Next(_surfaceTextures.Count)];
}
