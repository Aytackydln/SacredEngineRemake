using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Shaders;

/// <summary>Shader and fixed-function state used to create one graphics pipeline.</summary>
public sealed class Dx12GraphicsPipelineDefinition
{
    private readonly InputElementDescription[] _inputLayout;

    public Dx12GraphicsPipelineDefinition(
        Dx12PipelineKind kind,
        Dx12ShaderSource vertexShader,
        Dx12ShaderSource pixelShader,
        IEnumerable<InputElementDescription>? inputLayout,
        BlendDescription blendState,
        RasterizerDescription rasterizerState,
        DepthStencilDescription depthStencilState,
        bool usesDepthBuffer,
        uint sampleMask = uint.MaxValue,
        PrimitiveTopologyType primitiveTopologyType = PrimitiveTopologyType.Triangle,
        SampleDescription sampleDescription = default)
    {
        Kind = kind;
        VertexShader = vertexShader;
        PixelShader = pixelShader;
        _inputLayout = inputLayout?.ToArray() ?? [];
        BlendState = blendState;
        RasterizerState = rasterizerState;
        DepthStencilState = depthStencilState;
        UsesDepthBuffer = usesDepthBuffer;
        SampleMask = sampleMask;
        PrimitiveTopologyType = primitiveTopologyType;
        SampleDescription = sampleDescription.Count == 0 ? new SampleDescription(1, 0) : sampleDescription;
    }

    public Dx12PipelineKind Kind { get; }
    public Dx12ShaderSource VertexShader { get; }
    public Dx12ShaderSource PixelShader { get; }
    public IReadOnlyList<InputElementDescription> InputLayout => _inputLayout;
    public BlendDescription BlendState { get; }
    public RasterizerDescription RasterizerState { get; }
    public DepthStencilDescription DepthStencilState { get; }
    public bool UsesDepthBuffer { get; }
    public uint SampleMask { get; }
    public PrimitiveTopologyType PrimitiveTopologyType { get; }
    public SampleDescription SampleDescription { get; }

    internal InputElementDescription[] GetInputLayout() => _inputLayout;
}
