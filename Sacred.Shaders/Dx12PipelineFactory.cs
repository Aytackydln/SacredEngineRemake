using System.Collections.ObjectModel;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Shaders;

/// <summary>Compiles catalog definitions and creates their Direct3D 12 root signature and pipeline states.</summary>
public static class Dx12PipelineFactory
{
    public static Dx12CompiledPipelineGroup Compile(
        Dx12PipelineGroupDefinition definition,
        Func<Dx12ShaderSource, ReadOnlyMemory<byte>> shaderCompiler)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(shaderCompiler);

        var shaders = new Dictionary<Dx12ShaderSource, ReadOnlyMemory<byte>>();
        foreach (var pipeline in definition.Pipelines.Values)
        {
            CompileOnce(pipeline.VertexShader);
            CompileOnce(pipeline.PixelShader);
        }

        return new Dx12CompiledPipelineGroup(definition, shaders);

        void CompileOnce(Dx12ShaderSource shader)
        {
            if (!shaders.ContainsKey(shader))
                shaders.Add(shader, shaderCompiler(shader));
        }
    }

    public static Dx12CreatedPipelineGroup Create(
        ID3D12Device device,
        Dx12CompiledPipelineGroup compiled,
        Format backBufferFormat,
        Format depthBufferFormat = Format.Unknown)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(compiled);

        var rootDescription = compiled.Definition.CreateRootSignatureDescription();
        var rootSignature = device.CreateRootSignature(in rootDescription, RootSignatureVersion.Version1);
        var pipelines = new Dictionary<Dx12PipelineKind, ID3D12PipelineState>();
        try
        {
            foreach (var (kind, definition) in compiled.Definition.Pipelines)
            {
                var description = new GraphicsPipelineStateDescription
                {
                    RootSignature = rootSignature,
                    VertexShader = compiled.GetShader(definition.VertexShader),
                    PixelShader = compiled.GetShader(definition.PixelShader),
                    InputLayout = definition.GetInputLayout(),
                    BlendState = definition.BlendState,
                    RasterizerState = definition.RasterizerState,
                    DepthStencilState = definition.DepthStencilState,
                    SampleMask = definition.SampleMask,
                    PrimitiveTopologyType = definition.PrimitiveTopologyType,
                    RenderTargetFormats = [backBufferFormat],
                    DepthStencilFormat = definition.UsesDepthBuffer ? depthBufferFormat : Format.Unknown,
                    SampleDescription = definition.SampleDescription
                };
                pipelines.Add(kind, device.CreateGraphicsPipelineState(description));
            }

            return new Dx12CreatedPipelineGroup(rootSignature, pipelines);
        }
        catch
        {
            foreach (var pipeline in pipelines.Values)
                pipeline.Dispose();
            rootSignature.Dispose();
            throw;
        }
    }
}

public sealed class Dx12CompiledPipelineGroup
{
    private readonly IReadOnlyDictionary<Dx12ShaderSource, ReadOnlyMemory<byte>> _shaders;

    internal Dx12CompiledPipelineGroup(
        Dx12PipelineGroupDefinition definition,
        IDictionary<Dx12ShaderSource, ReadOnlyMemory<byte>> shaders)
    {
        Definition = definition;
        _shaders = new ReadOnlyDictionary<Dx12ShaderSource, ReadOnlyMemory<byte>>(shaders);
    }

    public Dx12PipelineGroupDefinition Definition { get; }

    internal ReadOnlyMemory<byte> GetShader(Dx12ShaderSource shader) => _shaders[shader];
}

public sealed class Dx12CreatedPipelineGroup
{
    internal Dx12CreatedPipelineGroup(
        ID3D12RootSignature rootSignature,
        IDictionary<Dx12PipelineKind, ID3D12PipelineState> pipelines)
    {
        RootSignature = rootSignature;
        Pipelines = new ReadOnlyDictionary<Dx12PipelineKind, ID3D12PipelineState>(pipelines);
    }

    public ID3D12RootSignature RootSignature { get; }
    public IReadOnlyDictionary<Dx12PipelineKind, ID3D12PipelineState> Pipelines { get; }
    public ID3D12PipelineState this[Dx12PipelineKind kind] => Pipelines[kind];
}
