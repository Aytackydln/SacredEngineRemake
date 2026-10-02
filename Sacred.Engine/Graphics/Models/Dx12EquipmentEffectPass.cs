using System;
using System.Numerics;
using Sacred.Assets.Paks.Texture;
using Sacred.Engine.Scene;
using Sacred.Particles;
using Sacred.Shaders;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Models;

/// <summary>CPU-deformed equipment particles retain their material pipelines independently of skeletal body drawing.</summary>
internal sealed class Dx12EquipmentEffectPass
{
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12ModelGeometryCache _geometryCache;
    private readonly Dx12ModelTextureCache _textureCache;
    private readonly GpuDescriptorHandle _srvHeapStart;
    private readonly int _descriptorSize;
    private readonly int _fallbackTextureSlot;
    private readonly ModelRootConstantsUpdater _rootConstants = new(ModelShaderLayout.RootParameterCount);
    private readonly ModelDescriptorTableUpdater _descriptorTables = new(ModelShaderLayout.RootParameterCount);
    private readonly ModelShaderConstantsUpdater _shaderConstants = new();
    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _transparentParticlePipeline;
    private ID3D12PipelineState? _denseParticlePipeline;
    private ID3D12PipelineState? _itemGlowPipeline;
    private ID3D12PipelineState? _itemParticleRgbPipeline;
    private ID3D12PipelineState? _itemParticleArgbPipeline;
    private ID3D12PipelineState? _itemParticleAlphaMaskPipeline;
    private ID3D12PipelineState? _itemGlowRgbPipeline;
    private ID3D12PipelineState? _itemGlowArgbPipeline;
    private ID3D12PipelineState? _itemGlowAlphaMaskPipeline;
    private bool _hdrOutput;
    public Dx12EquipmentEffectPass(ID3D12GraphicsCommandList commands, Dx12ModelGeometryCache geometry,
        Dx12ModelTextureCache textures, GpuDescriptorHandle heap, int descriptorSize, int fallback)
    { _commandList = commands; _geometryCache = geometry; _textureCache = textures; _srvHeapStart = heap; _descriptorSize = descriptorSize; _fallbackTextureSlot = fallback; }
    public void SetPipeline(Dx12CreatedPipelineGroup pipeline, bool hdrOutput)
    {
        _rootSignature = pipeline.RootSignature;
        _hdrOutput = hdrOutput;
        _transparentParticlePipeline = pipeline[Dx12PipelineKind.TransparentItemParticle];
        _denseParticlePipeline = pipeline[Dx12PipelineKind.DenseItemParticle];
        _itemGlowPipeline = pipeline[Dx12PipelineKind.ItemGlow];
        if (hdrOutput)
        {
            _itemParticleRgbPipeline = pipeline[Dx12PipelineKind.ItemParticleRgb];
            _itemParticleArgbPipeline = pipeline[Dx12PipelineKind.ItemParticleArgb];
            _itemParticleAlphaMaskPipeline = pipeline[Dx12PipelineKind.ItemParticleAlphaMask];
            _itemGlowRgbPipeline = pipeline[Dx12PipelineKind.ItemGlowRgb];
            _itemGlowArgbPipeline = pipeline[Dx12PipelineKind.ItemGlowArgb];
            _itemGlowAlphaMaskPipeline = pipeline[Dx12PipelineKind.ItemGlowAlphaMask];
        }
    }
    public void DisposePipeline()
    {
        _transparentParticlePipeline?.Dispose();
        _transparentParticlePipeline = null;
        _denseParticlePipeline?.Dispose();
        _denseParticlePipeline = null;
        _itemGlowPipeline?.Dispose();
        _itemGlowPipeline = null;
        _itemParticleRgbPipeline?.Dispose();
        _itemParticleRgbPipeline = null;
        _itemParticleArgbPipeline?.Dispose();
        _itemParticleArgbPipeline = null;
        _itemParticleAlphaMaskPipeline?.Dispose();
        _itemParticleAlphaMaskPipeline = null;
        _itemGlowRgbPipeline?.Dispose();
        _itemGlowRgbPipeline = null;
        _itemGlowArgbPipeline?.Dispose();
        _itemGlowArgbPipeline = null;
        _itemGlowAlphaMaskPipeline?.Dispose();
        _itemGlowAlphaMaskPipeline = null;
    }
    public unsafe void Record(
        SceneModel model,
        Matrix4x4 viewProjection,
        float modelSceneDepth,
        int frameIndex,
        float* constants)
    {
        var effects = model.EquipmentEffects;
        if (effects is null || _transparentParticlePipeline is null || _denseParticlePipeline is null || _itemGlowPipeline is null)
            return;

        if (!_geometryCache.TryGetOrRequest(effects.Mesh, frameIndex, out var mesh))
            return;
        _commandList.SetGraphicsRootSignature(_rootSignature);
        _rootConstants.Reset(); _descriptorTables.Reset();
        var vertexBufferView = mesh.VertexBufferViews[frameIndex];
        var indexBufferView = mesh.IndexBufferView;
        _commandList.IASetVertexBuffers(0, 1, &vertexBufferView);
        _commandList.IASetIndexBuffer(&indexBufferView);

        foreach (var surface in effects.Surfaces)
        {
            if (model.DisabledEquipmentEffects?.Contains((surface.TextureName, surface.TextureMode)) == true)
                continue;
            var texture = _textureCache.Get(surface.TextureName);
            if (texture is null || surface.IndexCount <= 0 || surface.IndexStart >= mesh.IndexCount)
                continue;

            var shaderKind = ParticleShaderCatalog.ForMode(surface.TextureMode);
            _commandList.SetPipelineState(_hdrOutput
                ? SelectHdrParticlePipeline(shaderKind, texture.ParticleEncoding)
                : shaderKind switch
                {
                    ParticleShaderKind.ItemGlow => _itemGlowPipeline,
                    ParticleShaderKind.DenseItemParticle => _denseParticlePipeline,
                    ParticleShaderKind.ItemParticle => _transparentParticlePipeline,
                    _ => throw new InvalidOperationException(
                        $"Particle mode {surface.TextureMode} selected unsupported model shader {shaderKind}.")
                });

            _shaderConstants.WriteModelBase(constants, viewProjection, model.Transform, surface.Color);
            _shaderConstants.WriteTextureFlags(
                constants + ModelShaderLayout.TextureFlagsOffset,
                (float)surface.TextureMode,
                modelSceneDepth,
                surface.Phase,
                animationTimeScale: 0.0f);
            SetRootConstantsIfChanged(
                ModelShaderLayout.ModelConstantsRootParameter,
                constants,
                ModelShaderLayout.ModelConstantsCount,
                0);
            SetDescriptorTableIfChanged(
                ModelShaderLayout.ModelTextureRootParameter,
                SrvGpuHandle(texture.SrvSlot));
            SetDescriptorTableIfChanged(
                ModelShaderLayout.ModelOverlayTextureRootParameter,
                SrvGpuHandle(_fallbackTextureSlot));
            var drawCount = Math.Min(surface.IndexCount, mesh.IndexCount - surface.IndexStart);
            _commandList.DrawIndexedInstanced((uint)drawCount, 1, (uint)surface.IndexStart, 0, 0);
        }
    }

    private ID3D12PipelineState SelectHdrParticlePipeline(
        ParticleShaderKind shaderKind,
        SacredTextureChannelEncoding encoding)
    {
        var glowVertex = shaderKind == ParticleShaderKind.ItemGlow;
        return (glowVertex, encoding) switch
        {
            (true, SacredTextureChannelEncoding.AlphaMask) => _itemGlowAlphaMaskPipeline!,
            (true, SacredTextureChannelEncoding.Argb) => _itemGlowArgbPipeline!,
            (true, _) => _itemGlowRgbPipeline!,
            (false, SacredTextureChannelEncoding.AlphaMask) => _itemParticleAlphaMaskPipeline!,
            (false, SacredTextureChannelEncoding.Argb) => _itemParticleArgbPipeline!,
            (false, _) => _itemParticleRgbPipeline!
        };
    }

    private unsafe void SetRootConstantsIfChanged(int parameter, float* constants, int count, int offset) =>
        _rootConstants.SetIfChanged(_commandList, parameter, constants, count, offset);
    private void SetDescriptorTableIfChanged(int parameter, GpuDescriptorHandle handle) =>
        _descriptorTables.SetIfChanged(_commandList, parameter, handle);
    private GpuDescriptorHandle SrvGpuHandle(int index) => _srvHeapStart + index * _descriptorSize;
}
