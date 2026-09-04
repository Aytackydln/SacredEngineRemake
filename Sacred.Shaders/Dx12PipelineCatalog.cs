using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Shaders;

/// <summary>Creates data-only descriptions for every pipeline backed by the Sacred shaders.</summary>
public static class Dx12PipelineCatalog
{
    private static readonly InputElementDescription[] ModelInputLayout =
    [
        new("POSITION", 0, Format.R32G32B32_Float, 0, 0),
        new("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
        new("TEXCOORD", 0, Format.R32G32_Float, 24, 0)
    ];

    private static readonly InputElementDescription[] ImGuiInputLayout =
    [
        new("POSITION", 0, Format.R32G32_Float, 0, 0),
        new("TEXCOORD", 0, Format.R32G32_Float, 8, 0),
        new("COLOR", 0, Format.R8G8B8A8_UNorm, 16, 0)
    ];

    public static Dx12PipelineGroupDefinition CreateImGui(bool hdrOutput)
    {
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(
                ImGuiShaderLayout.ConstantsRegister,
                0,
                ImGuiShaderLayout.ConstantsCount), ShaderVisibility.All),
            TextureTable(ImGuiShaderLayout.TextureRegister)
        };

        return new Dx12PipelineGroupDefinition(
            rootParameters,
            [CreateSampler(
                ImGuiShaderLayout.SamplerRegister,
                TextureAddressMode.Clamp,
                StaticBorderColor.TransparentBlack)],
            [new Dx12GraphicsPipelineDefinition(
                Dx12PipelineKind.ImGui,
                Dx12ShaderCatalog.ImGuiVertexShader,
                Dx12ShaderCatalog.GetImGuiPixelShader(hdrOutput),
                ImGuiInputLayout,
                CreatePremultipliedBlend(),
                RasterizerDescription.CullNone,
                DepthStencilDescription.None,
                usesDepthBuffer: false)]);
    }

    public static Dx12PipelineGroupDefinition CreateScreen(Dx12ShaderSet shaders)
    {
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(
                WorldQuadShaderLayout.RootConstantsRegister,
                0,
                WorldQuadShaderLayout.RootConstantsCount), ShaderVisibility.All),
            TextureTable(WorldQuadShaderLayout.TextureRegister)
        };

        return new Dx12PipelineGroupDefinition(
            rootParameters,
            [CreateSampler(
                WorldQuadShaderLayout.SamplerRegister,
                TextureAddressMode.Clamp,
                StaticBorderColor.TransparentBlack)],
            [Pipeline(
                Dx12PipelineKind.Terrain,
                shaders.QuadWorldVertexShader,
                shaders.QuadScreenPixelShader,
                BlendDescription.AlphaBlend,
                RasterizerDescription.CullNone,
                DepthStencilDescription.None,
                usesDepthBuffer: false)]);
    }

    public static Dx12PipelineGroupDefinition CreateTerrain(Dx12ShaderSet shaders)
    {
        var rootParameters = new[]
        {
            new(new RootConstants(
                WorldQuadShaderLayout.RootConstantsRegister,
                0,
                WorldQuadShaderLayout.RootConstantsCount), ShaderVisibility.All),
            TextureTable(WorldQuadShaderLayout.TextureRegister),
            new(RootParameterType.ShaderResourceView,
                new RootDescriptor(WorldQuadShaderLayout.WorldLightBufferRegister, 0), ShaderVisibility.Pixel)
        };

        // Sector images are atlas-like render targets. Clamping prevents bilinear samples
        // at an outer texel from blending with the opposite edge and producing moving seams.
        return new Dx12PipelineGroupDefinition(
            rootParameters,
            [CreateSampler(
                WorldQuadShaderLayout.SamplerRegister,
                TextureAddressMode.Clamp,
                StaticBorderColor.TransparentBlack)],
            [
                Pipeline(
                    Dx12PipelineKind.Terrain,
                    shaders.QuadWorldVertexShader,
                    shaders.QuadWorldPixelShader,
                    BlendDescription.AlphaBlend,
                    RasterizerDescription.CullNone,
                    DepthStencilDescription.None,
                    usesDepthBuffer: false),
                Pipeline(
                    Dx12PipelineKind.TerrainLiquidCover,
                    shaders.QuadWorldVertexShader,
                    shaders.QuadWorldPixelShader,
                    CreatePremultipliedBlend(),
                    RasterizerDescription.CullNone,
                    DepthStencilDescription.None,
                    usesDepthBuffer: false)
            ]);
    }

    public static Dx12PipelineGroupDefinition CreateStaticSprites(
        Dx12ShaderSet shaders,
        bool hdrOutput = false)
    {
        var rootParameters = new[]
        {
            new(new RootConstants(
                StaticSpriteShaderLayout.SceneConstantsRegister,
                0,
                StaticSpriteShaderLayout.SceneConstantsCount), ShaderVisibility.All),
            new(RootParameterType.ShaderResourceView,
                new RootDescriptor(StaticSpriteShaderLayout.InstanceBufferRegister, 0), ShaderVisibility.Vertex),
            TextureTable(StaticSpriteShaderLayout.FirstTextureRegister),
            new(RootParameterType.ShaderResourceView,
                new RootDescriptor(StaticSpriteShaderLayout.WorldLightBufferRegister, 0), ShaderVisibility.Pixel)
        };

        return new Dx12PipelineGroupDefinition(
            rootParameters,
            [CreateSampler(
                StaticSpriteShaderLayout.SamplerRegister,
                TextureAddressMode.Clamp,
                StaticBorderColor.TransparentBlack)],
            [
                Pipeline(
                    Dx12PipelineKind.StaticSpriteShadow,
                    shaders.StaticSpriteShadowVertexShader,
                    shaders.StaticSpriteShadowPixelShader,
                    hdrOutput ? CreatePremultipliedBlend() : BlendDescription.AlphaBlend,
                    RasterizerDescription.CullNone,
                    DepthStencilDescription.None,
                    usesDepthBuffer: false),
                Pipeline(
                    Dx12PipelineKind.StaticSprite,
                    shaders.StaticSpriteVertexShader,
                    shaders.StaticSpritePixelShader,
                    CreatePremultipliedBlend(),
                    RasterizerDescription.CullNone,
                    CreateLessEqualDepth(),
                    usesDepthBuffer: true),
                Pipeline(
                    Dx12PipelineKind.LiquidSprite,
                    shaders.StaticSpriteVertexShader,
                    shaders.StaticSpritePixelShader,
                    hdrOutput ? CreatePremultipliedBlend() : CreateLiquidSpriteBlend(),
                    RasterizerDescription.CullNone,
                    DepthStencilDescription.None,
                    usesDepthBuffer: false)
            ]);
    }

    public static Dx12PipelineGroupDefinition CreateLightHalos(Dx12ShaderSet shaders)
    {
        var rootParameters = new[]
        {
            new RootParameter(
                new RootConstants(
                    LightHaloShaderLayout.SceneConstantsRegister,
                    0,
                    LightHaloShaderLayout.SceneConstantsCount),
                ShaderVisibility.All),
            new RootParameter(
                RootParameterType.ShaderResourceView,
                new RootDescriptor(LightHaloShaderLayout.InstanceBufferRegister, 0),
                ShaderVisibility.Vertex),
            TextureTable(LightHaloShaderLayout.TextureRegister)
        };

        return new Dx12PipelineGroupDefinition(
            rootParameters,
            [CreateSampler(
                LightHaloShaderLayout.SamplerRegister,
                TextureAddressMode.Clamp,
                StaticBorderColor.TransparentBlack)],
            [Pipeline(
                Dx12PipelineKind.LightHalo,
                shaders.LightHaloVertexShader,
                shaders.LightHaloPixelShader,
                CreatePremultipliedBlend(),
                RasterizerDescription.CullNone,
                DepthStencilDescription.None,
                usesDepthBuffer: false)]);
    }

    public static Dx12PipelineGroupDefinition CreateModels(
        Dx12ShaderSet shaders,
        Dx12ModelPipelineOptions? options = null)
    {
        options ??= new Dx12ModelPipelineOptions();
        var rootParameters = new[]
        {
            new(new RootConstants(
                ModelShaderLayout.ModelConstantsRegister,
                0,
                ModelShaderLayout.ModelConstantsCount), ShaderVisibility.All),
            TextureTable(ModelShaderLayout.ModelTextureRegister),
            TextureTable(ModelShaderLayout.ModelOverlayTextureRegister),
            new(new RootConstants(
                ModelShaderLayout.SceneConstantsRegister,
                0,
                ModelShaderLayout.SceneConstantsCount), ShaderVisibility.All)
        };

        var depth = CreateLessEqualDepth();
        var transparentModelDepth = depth;
        transparentModelDepth.DepthWriteMask = DepthWriteMask.Zero;
        var particleDepth = depth;
        particleDepth.DepthWriteMask = DepthWriteMask.Zero;
        var particleBlend = CreateParticleBlend(options.HdrOutput);
        var shadowDepth = CreateShadowDepth();
        var pipelines = new List<Dx12GraphicsPipelineDefinition>
        {
            ModelPipeline(Dx12PipelineKind.ModelShadow, shaders.ModelShadowVertexShader, shaders.ModelShadowPixelShader,
                BlendDescription.AlphaBlend, RasterizerDescription.CullNone, shadowDepth),
            Pipeline(Dx12PipelineKind.GroundShadow, shaders.GroundShadowVertexShader, shaders.GroundShadowPixelShader,
                BlendDescription.AlphaBlend, RasterizerDescription.CullNone, shadowDepth, usesDepthBuffer: true),
            ModelPipeline(Dx12PipelineKind.StaticModel, shaders.ModelVertexShader, shaders.ModelPixelShader,
                BlendDescription.AlphaBlend, RasterizerDescription.CullClockwise, depth),
            ModelPipeline(Dx12PipelineKind.TransparentModel, shaders.ModelVertexShader, shaders.ModelPixelShader,
                BlendDescription.AlphaBlend, RasterizerDescription.CullClockwise, transparentModelDepth),
            ModelPipeline(Dx12PipelineKind.AnimatedModel, shaders.AnimatedModelVertexShader, shaders.AnimatedModelPixelShader,
                BlendDescription.AlphaBlend, RasterizerDescription.CullClockwise, depth),
            ModelPipeline(Dx12PipelineKind.EffectModel, shaders.EffectModelVertexShader, shaders.EffectModelPixelShader,
                BlendDescription.AlphaBlend, RasterizerDescription.CullClockwise, depth),
            ModelPipeline(Dx12PipelineKind.TransparentEffectModel, shaders.EffectModelVertexShader, shaders.EffectModelPixelShader,
                BlendDescription.AlphaBlend, RasterizerDescription.CullClockwise, transparentModelDepth),
            ModelPipeline(Dx12PipelineKind.TransparentItemParticle, shaders.ItemParticleVertexShader, shaders.ItemParticlePixelShader,
                particleBlend, RasterizerDescription.CullNone, particleDepth),
            ModelPipeline(Dx12PipelineKind.ItemGlow, shaders.ItemGlowVertexShader, shaders.ItemGlowPixelShader,
                particleBlend, RasterizerDescription.CullNone, particleDepth)
        };

        if (options.IncludeDenseParticle)
        {
            pipelines.Add(ModelPipeline(
                Dx12PipelineKind.DenseItemParticle,
                shaders.ItemParticleVertexShader,
                shaders.ItemParticlePixelShader,
                particleBlend,
                RasterizerDescription.CullNone,
                particleDepth));
        }

        if (options.IncludeInventoryUi)
        {
            pipelines.Add(ModelPipeline(
                Dx12PipelineKind.InventoryUi,
                shaders.InventoryUiVertexShader,
                shaders.InventoryUiPixelShader,
                BlendDescription.AlphaBlend,
                RasterizerDescription.CullNone,
                CreateDisabledDepth()));
        }

        return new Dx12PipelineGroupDefinition(
            rootParameters,
            [CreateSampler(
                ModelShaderLayout.ModelSamplerRegister,
                options.SamplerAddressMode,
                options.SamplerBorderColor)],
            pipelines);
    }

    private static Dx12GraphicsPipelineDefinition Pipeline(
        Dx12PipelineKind kind,
        Dx12ShaderSource vertexShader,
        Dx12ShaderSource pixelShader,
        BlendDescription blendState,
        RasterizerDescription rasterizerState,
        DepthStencilDescription depthStencilState,
        bool usesDepthBuffer) =>
        new(kind, vertexShader, pixelShader, null, blendState, rasterizerState, depthStencilState, usesDepthBuffer);

    private static Dx12GraphicsPipelineDefinition ModelPipeline(
        Dx12PipelineKind kind,
        Dx12ShaderSource vertexShader,
        Dx12ShaderSource pixelShader,
        BlendDescription blendState,
        RasterizerDescription rasterizerState,
        DepthStencilDescription depthStencilState) =>
        new(kind, vertexShader, pixelShader, ModelInputLayout, blendState, rasterizerState, depthStencilState, usesDepthBuffer: true);

    private static RootParameter TextureTable(int shaderRegister) =>
        new(new RootDescriptorTable
        {
            Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, (uint)shaderRegister, 0, 0)]
        }, ShaderVisibility.Pixel);

    private static StaticSamplerDescription CreateSampler(
        int shaderRegister,
        TextureAddressMode addressMode,
        StaticBorderColor borderColor) =>
        new((uint)shaderRegister, Filter.MinMagMipLinear, addressMode, addressMode, addressMode, 0.0f, 16,
            ComparisonFunction.Never, borderColor, 0.0f, float.MaxValue, ShaderVisibility.Pixel, 0);

    private static DepthStencilDescription CreateLessEqualDepth()
    {
        var depth = DepthStencilDescription.Default;
        depth.DepthFunc = ComparisonFunction.LessEqual;
        return depth;
    }

    private static DepthStencilDescription CreateDisabledDepth()
    {
        var depth = DepthStencilDescription.Default;
        depth.DepthEnable = false;
        depth.DepthWriteMask = DepthWriteMask.Zero;
        return depth;
    }

    private static DepthStencilDescription CreateShadowDepth()
    {
        var depth = DepthStencilDescription.Default;
        depth.DepthFunc = ComparisonFunction.Less;
        return depth;
    }

    private static BlendDescription CreatePremultipliedBlend()
    {
        var blend = BlendDescription.AlphaBlend;
        blend.RenderTarget[0].SourceBlend = Blend.One;
        blend.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
        blend.RenderTarget[0].SourceBlendAlpha = Blend.One;
        blend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
        return blend;
    }

    private static BlendDescription CreateLiquidSpriteBlend()
    {
        var blend = BlendDescription.AlphaBlend;
        blend.RenderTarget[0].SourceBlend = Blend.SourceAlpha;
        return blend;
    }

    private static BlendDescription CreateParticleBlend(bool hdrOutput)
    {
        var blend = BlendDescription.AlphaBlend;
        if (!hdrOutput)
        {
            blend.RenderTarget[0].DestinationBlend = Blend.One;
            blend.RenderTarget[0].DestinationBlendAlpha = Blend.One;
        }

        return blend;
    }

}
