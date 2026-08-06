using System.Collections.ObjectModel;
using Vortice.Direct3D12;

namespace Sacred.Shaders;

/// <summary>A root signature and the named pipeline states which use it.</summary>
public sealed class Dx12PipelineGroupDefinition
{
    private readonly RootParameter[] _rootParameters;
    private readonly StaticSamplerDescription[] _staticSamplers;

    public Dx12PipelineGroupDefinition(
        IEnumerable<RootParameter> rootParameters,
        IEnumerable<StaticSamplerDescription> staticSamplers,
        IEnumerable<Dx12GraphicsPipelineDefinition> pipelines,
        RootSignatureFlags rootSignatureFlags = RootSignatureFlags.AllowInputAssemblerInputLayout)
    {
        _rootParameters = rootParameters.ToArray();
        _staticSamplers = staticSamplers.ToArray();

        var pipelineDictionary = pipelines.ToDictionary(pipeline => pipeline.Kind);
        Pipelines = new ReadOnlyDictionary<Dx12PipelineKind, Dx12GraphicsPipelineDefinition>(pipelineDictionary);
        RootSignatureFlags = rootSignatureFlags;
    }

    public IReadOnlyList<RootParameter> RootParameters => _rootParameters;
    public IReadOnlyList<StaticSamplerDescription> StaticSamplers => _staticSamplers;
    public IReadOnlyDictionary<Dx12PipelineKind, Dx12GraphicsPipelineDefinition> Pipelines { get; }
    public RootSignatureFlags RootSignatureFlags { get; }

    internal RootSignatureDescription CreateRootSignatureDescription() =>
        new(RootSignatureFlags, _rootParameters, _staticSamplers);
}
