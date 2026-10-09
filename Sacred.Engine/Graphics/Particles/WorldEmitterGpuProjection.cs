using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Geometry;
using Sacred.World.Particles;

namespace Sacred.Engine.Graphics.Particles;

internal static class WorldEmitterGpuProjection
{
    public static float[] Constants(WorldGpuEmitter emitter, int set, SacredCamera camera, int width,
        int height, int offset, uint texture, uint encoding, bool halo = false)
    {
        var c = new float[ParticleGpuShaderLayout.ComputeConstants];
        Dx12ParticleKernels.UInt(c, 0, halo ? 1u : (uint)emitter.Definition.Capacity);
        Dx12ParticleKernels.UInt(c, 1, (uint)offset); Dx12ParticleKernels.UInt(c, 17, (uint)offset);
        Dx12ParticleKernels.UInt(c, 18, halo ? 1u : (uint)emitter.Sprite.AtlasColumns);
        Dx12ParticleKernels.UInt(c, 19, texture);
        var p = emitter.Projection;
        c[20] = p.HorizontalScale; c[21] = p.VerticalScale; c[22] = p.GroundDepthFactor; c[23] = p.HeightFactor;
        c[24] = emitter.Origin.X; c[25] = emitter.Origin.Y; c[26] = emitter.DepthAnchor.X; c[27] = emitter.DepthAnchor.Y;
        if (halo) { c[26] = emitter.Origin.X; c[27] = emitter.Origin.Y; }
        c[28] = emitter.HeightOffset;
        var draw = emitter.Definition.Draw!;
        c[ParticleGpuShaderLayout.HalfSizeMultiplierOffset] = halo ? 1 : draw.HalfSizeMultiplier;
        var additive = emitter.Definition.EmissionMode == 2 ? emitter.Parameters[set].Index == 0 : (draw.RawFlags & 1) != 0;
        Dx12ParticleKernels.UInt(c, 29, halo ? 2u | (emitter.Definition.Halo!.SourceColorOnly ? 4u : 0u) :
            (additive ? 2u : 0u) | ((draw.RawFlags & 0x10) != 0 ? 4u : 0u));
        Dx12ParticleKernels.UInt(c, 30, halo || draw.UsesColorTable ? 1u : draw.UsesParticleColor ? 2u :
            (draw.RawFlags & 2) == 0 ? 3u : 0u);
        Dx12ParticleKernels.UInt(c, 31, halo || draw.UsesRandomAtlasCell ? 1u : 0u);
        var screen = IsometricProjection.CreateScreenTransform(camera.ViewCenter, camera.ViewportZoom, width, height);
        c[32] = (float)screen.OriginX; c[33] = (float)screen.OriginY; c[34] = screen.Zoom;
        c[35] = WorldPainterDepth.FromWorld(camera.ViewCenter); Dx12ParticleKernels.UInt(c, 36, encoding);
        Dx12ParticleKernels.UInt(c, 38, !halo && draw.UsesRotation ? 1u : 0u);
        Dx12ParticleKernels.UInt(c, 39, !halo && draw.AnchoredAtBottom ? 1u : 0u);
        return c;
    }
}
