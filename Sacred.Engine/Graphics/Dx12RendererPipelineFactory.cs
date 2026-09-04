using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics;

/// <summary>Connects the renderer's shader compiler to the shared Sacred pipeline catalog.</summary>
internal static class Dx12RendererPipelineFactory
{
    public static Dx12CompiledPipelineGroup CompileScreen(Dx12ShaderSet shaders) =>
        Dx12PipelineFactory.Compile(Dx12PipelineCatalog.CreateScreen(shaders), Dx12ShaderCompiler.CompileShader);

    public static Dx12CompiledPipelineGroup CompileTerrain(Dx12ShaderSet shaders) =>
        Dx12PipelineFactory.Compile(Dx12PipelineCatalog.CreateTerrain(shaders), Dx12ShaderCompiler.CompileShader);

    public static Dx12CompiledRendererPipelines Compile(Dx12ShaderSet shaders, bool hdrOutput) => new(
        CompileTerrain(shaders),
        Dx12PipelineFactory.Compile(
            Dx12PipelineCatalog.CreateStaticSprites(shaders, hdrOutput),
            Dx12ShaderCompiler.CompileShader),
        Dx12PipelineFactory.Compile(
            Dx12PipelineCatalog.CreateLightHalos(shaders),
            Dx12ShaderCompiler.CompileShader),
        Dx12PipelineFactory.Compile(
            Dx12PipelineCatalog.CreateModels(shaders, new Dx12ModelPipelineOptions { HdrOutput = hdrOutput }),
            Dx12ShaderCompiler.CompileShader),
        Dx12PipelineFactory.Compile(
            Dx12PipelineCatalog.CreateImGui(hdrOutput),
            Dx12ShaderCompiler.CompileShader));

    public static Dx12CreatedPipelineGroup Create(
        ID3D12Device device,
        Dx12CompiledPipelineGroup pipelines,
        Format backBufferFormat,
        Format depthBufferFormat = Format.Unknown) =>
        Dx12PipelineFactory.Create(device, pipelines, backBufferFormat, depthBufferFormat);
}

internal sealed record Dx12CompiledRendererPipelines(
    Dx12CompiledPipelineGroup Terrain,
    Dx12CompiledPipelineGroup StaticSprites,
    Dx12CompiledPipelineGroup LightHalos,
    Dx12CompiledPipelineGroup Models,
    Dx12CompiledPipelineGroup ImGui);
