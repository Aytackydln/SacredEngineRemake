using System;
using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics;

/// <summary>Owns construction details for the renderer's shader-specific pipelines.</summary>
internal static class Dx12RendererPipelineFactory
{
    public static Dx12CompiledShaderSet Compile(Dx12ShaderSet shaders) => new(
        Dx12ShaderCompiler.CompileShader(shaders.QuadWorldVertexShader),
        Dx12ShaderCompiler.CompileShader(shaders.QuadWorldPixelShader),
        Dx12ShaderCompiler.CompileShader(shaders.StaticSpriteVertexShader),
        Dx12ShaderCompiler.CompileShader(shaders.StaticSpritePixelShader),
        Dx12ShaderCompiler.CompileShader(shaders.ModelVertexShader),
        Dx12ShaderCompiler.CompileShader(shaders.ModelPixelShader),
        Dx12ShaderCompiler.CompileShader(shaders.AnimatedModelVertexShader),
        Dx12ShaderCompiler.CompileShader(shaders.AnimatedModelPixelShader),
        Dx12ShaderCompiler.CompileShader(shaders.EffectModelVertexShader),
        Dx12ShaderCompiler.CompileShader(shaders.EffectModelPixelShader),
        Dx12ShaderCompiler.CompileShader(shaders.ItemParticleVertexShader),
        Dx12ShaderCompiler.CompileShader(shaders.ItemParticlePixelShader));

    public static TerrainPipelines CreateTerrain(ID3D12Device device, Dx12CompiledShaderSet shaders, Format backBufferFormat)
    {
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(WorldQuadShaderLayout.RootConstantsRegister, 0, WorldQuadShaderLayout.RootConstantsCount), ShaderVisibility.All),
            new RootParameter(new RootDescriptorTable { Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, 0, 0, 0)] }, ShaderVisibility.Pixel)
        };
        // Sector images are atlas-like render targets, not tiling textures. Wrapping makes
        // bilinear samples at an outer texel blend with the opposite edge, producing seams
        // that move as the camera crosses fractional pixel positions.
        var samplers = new[] { CreateSampler(TextureAddressMode.Clamp, StaticBorderColor.TransparentBlack) };
        var rootDescription = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rootParameters, samplers);
        var rootSignature = device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = rootSignature,
            VertexShader = shaders.QuadWorldVertexShader,
            PixelShader = shaders.QuadWorldPixelShader,
            BlendState = BlendDescription.AlphaBlend,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = DepthStencilDescription.None,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [backBufferFormat],
            DepthStencilFormat = Format.Unknown,
            SampleDescription = new SampleDescription(1, 0)
        };
        var basePipeline = device.CreateGraphicsPipelineState(description);
        var premultipliedBlend = BlendDescription.AlphaBlend;
        premultipliedBlend.RenderTarget[0].SourceBlend = Blend.One;
        premultipliedBlend.RenderTarget[0].DestinationBlend = Blend.InverseSourceAlpha;
        premultipliedBlend.RenderTarget[0].SourceBlendAlpha = Blend.One;
        premultipliedBlend.RenderTarget[0].DestinationBlendAlpha = Blend.InverseSourceAlpha;
        description.BlendState = premultipliedBlend;
        return new TerrainPipelines(rootSignature, basePipeline, device.CreateGraphicsPipelineState(description));
    }

    public static StaticSpritePipelines CreateStaticSprites(
        ID3D12Device device, Dx12CompiledShaderSet shaders, Format backBufferFormat, Format depthBufferFormat, int maxTextures)
    {
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(StaticSpriteShaderLayout.SceneConstantsRegister, 0, StaticSpriteShaderLayout.SceneConstantsCount), ShaderVisibility.All),
            new RootParameter(RootParameterType.ShaderResourceView, new RootDescriptor(StaticSpriteShaderLayout.InstanceBufferRegister, 0), ShaderVisibility.Vertex),
            new RootParameter(new RootDescriptorTable { Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, (uint)maxTextures, StaticSpriteShaderLayout.FirstTextureRegister, 0, 0)] }, ShaderVisibility.Pixel)
        };
        var samplers = new[] { CreateSampler(TextureAddressMode.Clamp, StaticBorderColor.TransparentBlack) };
        var rootDescription = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rootParameters, samplers);
        var rootSignature = device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);
        var depthStencil = DepthStencilDescription.Default;
        depthStencil.DepthFunc = ComparisonFunction.LessEqual;
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = rootSignature,
            VertexShader = shaders.StaticSpriteVertexShader,
            PixelShader = shaders.StaticSpritePixelShader,
            BlendState = BlendDescription.AlphaBlend,
            RasterizerState = RasterizerDescription.CullNone,
            DepthStencilState = depthStencil,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [backBufferFormat],
            DepthStencilFormat = depthBufferFormat,
            SampleDescription = new SampleDescription(1, 0)
        };
        var spritePipeline = device.CreateGraphicsPipelineState(description);
        description.DepthStencilState = DepthStencilDescription.None;
        description.DepthStencilFormat = Format.Unknown;
        var liquidBlend = BlendDescription.AlphaBlend;
        liquidBlend.RenderTarget[0].SourceBlend = Blend.SourceAlpha;
        description.BlendState = liquidBlend;
        return new StaticSpritePipelines(rootSignature, spritePipeline, device.CreateGraphicsPipelineState(description));
    }

    public static ModelPipelines CreateModels(ID3D12Device device, Dx12CompiledShaderSet shaders, Format backBufferFormat, Format depthBufferFormat)
    {
        var rootParameters = new[]
        {
            new RootParameter(new RootConstants(ModelShaderLayout.ModelConstantsRegister, 0, ModelShaderLayout.ModelConstantsCount), ShaderVisibility.All),
            new RootParameter(new RootDescriptorTable { Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, ModelShaderLayout.ModelTextureRegister, 0, 0)] }, ShaderVisibility.Pixel),
            new RootParameter(new RootDescriptorTable { Ranges = [new DescriptorRange(DescriptorRangeType.ShaderResourceView, 1, ModelShaderLayout.ModelOverlayTextureRegister, 0, 0)] }, ShaderVisibility.Pixel),
            new RootParameter(new RootConstants(ModelShaderLayout.SceneConstantsRegister, 0, ModelShaderLayout.SceneConstantsCount), ShaderVisibility.All)
        };
        var samplers = new[] { CreateSampler(TextureAddressMode.Clamp, StaticBorderColor.TransparentBlack) };
        var rootDescription = new RootSignatureDescription(RootSignatureFlags.AllowInputAssemblerInputLayout, rootParameters, samplers);
        var rootSignature = device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);
        var depthStencil = DepthStencilDescription.Default;
        depthStencil.DepthFunc = ComparisonFunction.LessEqual;
        var description = new GraphicsPipelineStateDescription
        {
            RootSignature = rootSignature,
            VertexShader = shaders.ModelVertexShader,
            PixelShader = shaders.ModelPixelShader,
            InputLayout = new[]
            {
                new InputElementDescription("POSITION", 0, Format.R32G32B32_Float, 0, 0),
                new InputElementDescription("NORMAL", 0, Format.R32G32B32_Float, 12, 0),
                new InputElementDescription("TEXCOORD", 0, Format.R32G32_Float, 24, 0)
            },
            BlendState = BlendDescription.AlphaBlend,
            RasterizerState = RasterizerDescription.CullClockwise,
            DepthStencilState = depthStencil,
            SampleMask = uint.MaxValue,
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            RenderTargetFormats = [backBufferFormat],
            DepthStencilFormat = depthBufferFormat,
            SampleDescription = new SampleDescription(1, 0)
        };
        var basePipeline = device.CreateGraphicsPipelineState(description);
        description.VertexShader = shaders.AnimatedModelVertexShader;
        description.PixelShader = shaders.AnimatedModelPixelShader;
        var animatedPipeline = device.CreateGraphicsPipelineState(description);
        description.VertexShader = shaders.EffectModelVertexShader;
        description.PixelShader = shaders.EffectModelPixelShader;
        var effectPipeline = device.CreateGraphicsPipelineState(description);

        var particleDepthStencil = depthStencil;
        particleDepthStencil.DepthWriteMask = DepthWriteMask.Zero;
        var particleBlend = BlendDescription.AlphaBlend;
        particleBlend.RenderTarget[0].DestinationBlend = Blend.One;
        particleBlend.RenderTarget[0].DestinationBlendAlpha = Blend.One;
        description.VertexShader = shaders.ItemParticleVertexShader;
        description.PixelShader = shaders.ItemParticlePixelShader;
        description.BlendState = particleBlend;
        description.RasterizerState = RasterizerDescription.CullNone;
        description.DepthStencilState = particleDepthStencil;
        return new ModelPipelines(
            rootSignature,
            basePipeline,
            animatedPipeline,
            effectPipeline,
            device.CreateGraphicsPipelineState(description));
    }

    private static StaticSamplerDescription CreateSampler(TextureAddressMode addressMode, StaticBorderColor borderColor) =>
        new(0, Filter.MinMagMipLinear, addressMode, addressMode, addressMode, 0.0f, 16, ComparisonFunction.Never, borderColor, 0.0f, float.MaxValue, ShaderVisibility.Pixel, 0);
}

internal sealed record TerrainPipelines(ID3D12RootSignature RootSignature, ID3D12PipelineState Base, ID3D12PipelineState LiquidCover);
internal sealed record StaticSpritePipelines(ID3D12RootSignature RootSignature, ID3D12PipelineState Static, ID3D12PipelineState Liquid);
internal sealed record ModelPipelines(
    ID3D12RootSignature RootSignature,
    ID3D12PipelineState Static,
    ID3D12PipelineState Animated,
    ID3D12PipelineState Effect,
    ID3D12PipelineState Particle);
internal sealed record Dx12CompiledShaderSet(
    ReadOnlyMemory<byte> QuadWorldVertexShader,
    ReadOnlyMemory<byte> QuadWorldPixelShader,
    ReadOnlyMemory<byte> StaticSpriteVertexShader,
    ReadOnlyMemory<byte> StaticSpritePixelShader,
    ReadOnlyMemory<byte> ModelVertexShader,
    ReadOnlyMemory<byte> ModelPixelShader,
    ReadOnlyMemory<byte> AnimatedModelVertexShader,
    ReadOnlyMemory<byte> AnimatedModelPixelShader,
    ReadOnlyMemory<byte> EffectModelVertexShader,
    ReadOnlyMemory<byte> EffectModelPixelShader,
    ReadOnlyMemory<byte> ItemParticleVertexShader,
    ReadOnlyMemory<byte> ItemParticlePixelShader);
