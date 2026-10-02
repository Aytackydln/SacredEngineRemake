using System;
using System.Runtime.InteropServices;
using Sacred.Engine.Scene;
using Sacred.Shaders;
using Vortice.Direct3D12;
using Vortice.DXGI;

namespace Sacred.Engine.Graphics.Skinning;

/// <summary>Shared skeletal draw bindings and frame-safe scene constants. No palette writes occur here.</summary>
internal sealed class Dx12SkinDrawBindings : IDisposable
{
    private readonly Dx12SkinPreparationCache _cache;
    private readonly ID3D12Device _device;
    private readonly Dx12TextureUploader _uploader;
    private readonly ID3D12Resource?[] _sceneBuffers = new ID3D12Resource[Dx12DeviceContext.FrameCount];
    private Dx12CreatedPipelineGroup? _pipelines;
    public bool IsReady => _pipelines is not null;
    public Dx12SkinDrawBindings(Dx12SkinPreparationCache cache, ID3D12Device device, Dx12TextureUploader uploader)
    { _cache = cache; _device = device; _uploader = uploader; }
    public ID3D12RootSignature Root => _pipelines!.RootSignature;
    public ID3D12PipelineState Pipeline(Dx12PipelineKind kind) => _pipelines![kind];
    public void CreatePipelines(bool hdr, Format target, Format depth)
    {
        var compiled = Dx12PipelineFactory.Compile(SkinningShaderLayout.CreateModels(hdr ? Dx12ShaderCatalog.Hdr : Dx12ShaderCatalog.Sdr, hdr), Dx12ShaderCompiler.CompileShader);
        var created = Dx12PipelineFactory.Create(_device, compiled, target, depth);
        DisposePipelines(); _pipelines = created;
    }
    public void DisposePipelines()
    { if (_pipelines is null) return; foreach (var p in _pipelines.Pipelines.Values) p.Dispose(); _pipelines.RootSignature.Dispose(); _pipelines = null; }
    public void UploadScene(ReadOnlySpan<float> values, int slot)
    {
        Span<byte> bytes = stackalloc byte[256]; bytes.Clear(); MemoryMarshal.AsBytes(values).CopyTo(bytes);
        if (_sceneBuffers[slot] is null) _sceneBuffers[slot] = _uploader.CreateUploadBuffer(bytes);
        else Dx12TextureUploader.UpdateUploadBuffer(_sceneBuffers[slot]!, bytes);
    }
    public bool TryGet(SceneModel model, int slot, out ModelGpuSkinSource source, out ModelGpuSkinInstance instance) =>
        _cache.TryGet(model.Geometry, slot, out source, out instance);
    public void Bind(ID3D12GraphicsCommandList commands, ModelGpuSkinSource source, ModelGpuSkinInstance instance, int slot, bool scene)
    {
        commands.SetGraphicsRootShaderResourceView(SkinningShaderLayout.VertexRoot, source.Vertices.GPUVirtualAddress);
        commands.SetGraphicsRootShaderResourceView(SkinningShaderLayout.InfluenceRoot, source.Influences.GPUVirtualAddress);
        commands.SetGraphicsRootShaderResourceView(SkinningShaderLayout.ProjectionRoot, source.Projection.GPUVirtualAddress);
        var address = instance.Buffer(slot)!.GPUVirtualAddress;
        commands.SetGraphicsRootShaderResourceView(SkinningShaderLayout.RegularRoot, address);
        commands.SetGraphicsRootShaderResourceView(SkinningShaderLayout.RigidRoot, address + (ulong)instance.PaletteStrideBytes);
        if (scene) commands.SetGraphicsRootConstantBufferView(ModelShaderLayout.SceneConstantsRootParameter, _sceneBuffers[slot]!.GPUVirtualAddress);
    }
    public void Dispose() { DisposePipelines(); foreach (var buffer in _sceneBuffers) buffer?.Dispose(); }
}
