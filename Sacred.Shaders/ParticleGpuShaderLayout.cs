using System.Text;
using Vortice.Direct3D12;

namespace Sacred.Shaders;

/// <summary>Compute constants and direct sorted-particle draw, reusing the existing sprite pixel functions.</summary>
public static class ParticleGpuShaderLayout
{
    public const int ComputeConstants = 40;
    private static byte[] Source()
    {
        string Read(EmbeddedResource_Shaders resource) => Encoding.UTF8.GetString(EmbeddedShaderAssemblyReloader.ReadAllBytes(resource.GetResourceName()));
        var common = Read(EmbeddedResource_Shaders.SacredSpriteCommon_hlsl);
        common = common.Replace("StructuredBuffer<SpriteInstance> instances : register(t0);", """
            struct ParticleDraw {
                SpriteInstance sprite; float sort_depth; int tile_x; int tile_y; int draw_order;
                uint sequence; uint encoding; uint valid;
            };
            StructuredBuffer<ParticleDraw> particle_draws : register(t4);
            """);
        common = common.Replace("float depth : TEXCOORD1;", "float depth : TEXCOORD1;\n    nointerpolation uint texture_encoding : TEXCOORD13;")
            .Replace("SpriteInstance instance = instances[instance_id];", """
                if (particle_draws[instance_id].valid == 0) {
                    vertex_output empty = (vertex_output)0; empty.position = float4(-2,-2,0,1); return empty;
                }
                SpriteInstance instance = particle_draws[instance_id].sprite;
                """)
            .Replace("output.depth = depth;", "output.depth = depth;\n    output.texture_encoding = particle_draws[instance_id].encoding;");
        return Encoding.UTF8.GetBytes(Read(EmbeddedResource_Shaders.HdrCommon_hlsl) + "\n" + common + "\n" +
            Read(EmbeddedResource_Shaders.SacredStaticSpriteCommon_hlsl) + "\n" +
            Read(EmbeddedResource_Shaders.SacredStaticSpriteUnlit_hlsl) + "\n" + Read(EmbeddedResource_Shaders.SacredGpuParticles_hlsl));
    }
    public static Dx12ShaderSource Shader(string entry, string target) => new("SacredGpuParticles", [Source], entry, target) { StrictFloat = true };
    public static RootSignatureDescription ComputeRoot() => new(RootSignatureFlags.None,
        [new(new RootConstants(1,0,ComputeConstants),ShaderVisibility.All),
         new(RootParameterType.UnorderedAccessView,new RootDescriptor(0,0),ShaderVisibility.All),
         new(RootParameterType.UnorderedAccessView,new RootDescriptor(1,0),ShaderVisibility.All),
         new(RootParameterType.ShaderResourceView,new RootDescriptor(5,0),ShaderVisibility.All),
         new(RootParameterType.ShaderResourceView,new RootDescriptor(6,0),ShaderVisibility.All),
         new(RootParameterType.ShaderResourceView,new RootDescriptor(7,0),ShaderVisibility.All)], []);
    public static Dx12PipelineGroupDefinition Draw(bool hdr)
    {
        var original = Dx12PipelineCatalog.CreateStaticSprites(hdr ? Dx12ShaderCatalog.Hdr : Dx12ShaderCatalog.Sdr,hdr);
        var p = original.Pipelines[Dx12PipelineKind.PostModelTransparentUnlitStaticSprite];
        var roots = original.RootParameters.ToArray();
        roots[1] = new(RootParameterType.ShaderResourceView,new RootDescriptor(4,0),ShaderVisibility.Vertex);
        return new(roots,original.StaticSamplers,[new(Dx12PipelineKind.PostModelTransparentUnlitStaticSprite,
            Shader("vs_main","vs_6_6"),Shader(hdr?"ps_gpu_hdr":"ps_gpu_sdr","ps_6_6"),p.InputLayout,p.BlendState,
            p.RasterizerState,p.DepthStencilState,p.UsesDepthBuffer,p.RenderTargetFormat)],original.RootSignatureFlags);
    }
}

