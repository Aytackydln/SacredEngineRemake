using System.Text;
using Vortice.Direct3D12;

namespace Sacred.Shaders;

/// <summary>Skeletal variants retain model/material parameters; b1 uses a CBV to fit the root budget.</summary>
public static class SkinningShaderLayout
{
    public const int VertexRoot = 5, InfluenceRoot = 6, ProjectionRoot = 7, RegularRoot = 8, RigidRoot = 9;
    public static Dx12ShaderSource VertexVariant(Dx12ShaderSource original) => new(original.Name + "Skinned",
        [() => EmbeddedShaderAssemblyReloader.ReadAllBytes(EmbeddedResource_Shaders.SacredSkinning_hlsl.GetResourceName()),
         () => Encoding.UTF8.GetBytes("#define SACRED_SKINNING 1\n"), original.ReadAllBytes], original.EntryPoint, original.Target);

    public static Dx12PipelineGroupDefinition CreateModels(Dx12ShaderSet shaders, bool hdr)
    {
        var original = Dx12PipelineCatalog.CreateModels(shaders, new() { HdrOutput = hdr });
        var roots = original.RootParameters.ToList();
        roots[ModelShaderLayout.SceneConstantsRootParameter] = new(RootParameterType.ConstantBufferView, new RootDescriptor(1, 0), ShaderVisibility.All);
        for (var register = 3; register <= 7; register++)
            roots.Add(new(RootParameterType.ShaderResourceView, new RootDescriptor((uint)register, 0), ShaderVisibility.Vertex));
        var kinds = new[] { Dx12PipelineKind.StaticModel, Dx12PipelineKind.TransparentModel, Dx12PipelineKind.AnimatedModel,
            Dx12PipelineKind.EffectModel, Dx12PipelineKind.TransparentEffectModel, Dx12PipelineKind.ModelShadow };
        var variants = new Dictionary<Dx12ShaderSource, Dx12ShaderSource>();
        var pipelines = kinds.Select(kind => {
            var p = original.Pipelines[kind];
            if (!variants.TryGetValue(p.VertexShader, out var vertex)) variants.Add(p.VertexShader, vertex = VertexVariant(p.VertexShader));
            return new Dx12GraphicsPipelineDefinition(kind, vertex, p.PixelShader, p.InputLayout, p.BlendState,
                p.RasterizerState, p.DepthStencilState, p.UsesDepthBuffer, p.RenderTargetFormat);
        });
        return new(roots, original.StaticSamplers, pipelines);
    }
}
