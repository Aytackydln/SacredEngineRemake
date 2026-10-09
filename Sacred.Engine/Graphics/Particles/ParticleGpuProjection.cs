using System;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace Sacred.Engine.Graphics.Particles;

internal static class ParticleGpuProjection
{
    public static float[] Constants(WorldGpuParticleBatch group, SacredCamera camera, int width, int height,
        int offset, uint texture, uint encoding)
    {
        var c = Dx12ParticleKernels.Constants(group.Batch);
        Dx12ParticleKernels.UInt(c,1,(uint)offset);
        Dx12ParticleKernels.UInt(c,17,(uint)offset);
        Dx12ParticleKernels.UInt(c,18,(uint)group.Sprite.AtlasColumns);
        Dx12ParticleKernels.UInt(c,19,texture);
        var p = group.Projection;
        c[20] = p.HorizontalScale; c[21] = p.VerticalScale; c[22] = p.GroundDepthFactor; c[23] = p.HeightFactor;
        c[24] = group.Origin.X; c[25] = group.Origin.Y; c[26] = group.DepthAnchor.X; c[27] = group.DepthAnchor.Y;
        c[28] = group.HeightOffset;
        var draw = group.Definition.Draw!;
        c[ParticleGpuShaderLayout.HalfSizeMultiplierOffset] = draw.HalfSizeMultiplier;
        var additive = group.Definition.EmissionMode == 2 ? group.Batch.Parameters.Index == 0 : (draw.RawFlags & 1) != 0;
        Dx12ParticleKernels.UInt(c,29,(additive ? 2u : 0u) | ((draw.RawFlags & 0x10) != 0 ? 4u : 0u));
        Dx12ParticleKernels.UInt(c,30,draw.UsesColorTable ? 1u : draw.UsesParticleColor ? 2u :
            (draw.RawFlags & 2) == 0 ? 3u : 0u);
        Dx12ParticleKernels.UInt(c,31,draw.UsesRandomAtlasCell ? 1u : 0u);
        var screen = IsometricProjection.CreateScreenTransform(camera.ViewCenter,camera.ViewportZoom,width,height);
        c[32] = (float)screen.OriginX; c[33] = (float)screen.OriginY; c[34] = screen.Zoom;
        c[35] = WorldPainterDepth.FromWorld(camera.ViewCenter);
        Dx12ParticleKernels.UInt(c,36,encoding);
        Dx12ParticleKernels.UInt(c,38,draw.UsesRotation ? 1u : 0u);
        Dx12ParticleKernels.UInt(c,39,draw.AnchoredAtBottom ? 1u : 0u);
        return c;
    }
    public static ParticleGpuDraw Cpu(WorldParticle particle, SacredCamera camera, int width, int height,
        uint texture, uint encoding, uint sequence)
    {
        var screen = IsometricProjection.CreateScreenTransform(camera.ViewCenter,camera.ViewportZoom,width,height);
        var anchor = IsometricProjection.WorldToIso(particle.WorldX,particle.WorldY)+IsometricProjection.TileAnchorOffset;
        var rect = screen.ToScreen(anchor.X-particle.Size*.5f,anchor.Y-particle.Height-particle.RenderHeight*.5f);
        var depth = particle.PainterDepthKey ?? WorldPainterDepth.FromWorld(new(particle.WorldX,particle.WorldY))+particle.Height/96;
        var color = particle.Color;
        return new()
        {
            Sprite = new(rect.X,rect.Y,screen.Scale(particle.Size),screen.Scale(particle.RenderHeight),
                Math.Clamp(.5f-(depth-WorldPainterDepth.FromWorld(camera.ViewCenter))/4096,.2f,.72f),
                texture,(uint)particle.Sprite.FrameCount,(uint)particle.AtlasCell,0,0,
                1u|(particle.Additive?2u:0u)|(particle.SourceColorOnly?4u:0u),0,0,
                ((color>>16)&255)/255f,((color>>8)&255)/255f,(color&255)/255f,particle.Opacity,
                (uint)particle.Sprite.AtlasColumns,(uint)particle.Sprite.AtlasRows) { ParticleRotation=particle.Rotation },
            SortDepth=depth,TileX=(int)MathF.Floor(particle.WorldX),TileY=(int)MathF.Floor(particle.WorldY),
            DrawOrder=particle.DrawOrder,Sequence=sequence,Encoding=encoding,Valid=1
        };
    }
}
