using System.Numerics;
using Sacred.Granny.Meshes;
using Sacred.Inventory.Effects;
using Sacred.Particles;
using Sacred.Shaders;
using Vortice.Direct3D12;

internal static class NativeQuadVerification
{
    public static void Run(EquipmentEffectScene scene, SacredModelEffectDefinition definition)
    {
        var blend = Dx12PipelineCatalog.CreateModels(Dx12ShaderCatalog.Sdr)
            .Pipelines[Dx12PipelineKind.TransparentItemParticle].BlendState.RenderTarget[0];
        if (blend.SourceBlend != Blend.One || blend.DestinationBlend != Blend.One)
            throw new InvalidOperationException("Native SDR premultiplied output requires One/One blending");
        var quad = 0;
        foreach (var surface in scene.Surfaces.Where(
                     surface => surface.TextureMode == ParticleTextureMode.NativeModelColored))
        for (var indexOffset = 0; indexOffset < surface.IndexCount; indexOffset += 6)
        {
            var rgb = CenterColor(scene.Mesh, surface.IndexStart + indexOffset);
            // Native strip BL/TL/BR and BR/TL/TR crosses the luminous texture center
            // along the green-bearing corners. Testing the result catches diagonal regressions.
            var halo = definition.Kind == SacredModelEffectKind.Streak && surface.Color.W < 0.5f;
            var expected = definition.Kind == SacredModelEffectKind.Whip ? new Vector3(32, 64, 128)
                : halo ? new Vector3(16, 32, 64) : new Vector3(64, 128, 192);
            if (Vector3.Distance(rgb, expected / 255f) > 1e-5f)
                throw new InvalidOperationException($"FAILED native {definition.Kind} quad {quad} center RGB {rgb}");
            quad++;
        }
        Console.WriteLine($"PASS {definition.Kind} native triangle interpolation on every quad");
    }

    private static Vector3 CenterColor(Mesh mesh, int firstIndex)
    {
        for (var triangle = 0; triangle < 2; triangle++)
        {
            var a = mesh.Vertices[mesh.Indices[firstIndex + triangle * 3]];
            var b = mesh.Vertices[mesh.Indices[firstIndex + triangle * 3 + 1]];
            var c = mesh.Vertices[mesh.Indices[firstIndex + triangle * 3 + 2]];
            var pa = new Vector2(a.Normal.X, a.Normal.Y);
            var pb = new Vector2(b.Normal.X, b.Normal.Y);
            var pc = new Vector2(c.Normal.X, c.Normal.Y);
            var det = Cross(pb - pa, pc - pa);
            var wb = Cross(-pa, pc - pa) / det;
            var wc = Cross(pb - pa, -pa) / det;
            if (wb >= 0 && wc >= 0 && wb + wc <= 1)
                return Color(a) * (1 - wb - wc) + Color(b) * wb + Color(c) * wc;
        }
        throw new InvalidOperationException("Native quad does not cover its center");
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    private static Vector3 Color(VertexPositionNormalTexture vertex)
    {
        var rgb = (uint)vertex.Normal.Z - 1;
        return new Vector3(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255) / 255f;
    }
}
