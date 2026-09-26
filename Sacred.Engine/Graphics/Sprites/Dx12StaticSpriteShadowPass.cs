using System;
using System.Numerics;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Scene;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Records file-authored soft shadows beneath static world sprites.</summary>
internal sealed class Dx12StaticSpriteShadowPass
{
    // cWorldView0::renderShadow projects directional billboard shadows by one authored
    // extent toward +X/-Y in screen space. It does not rotate them with the 3D sun.
    private static readonly Vector2 DirectionalProjection = new(1.0f, -1.0f);

    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly GpuDescriptorHandle _srvHeapGpuStart;
    private readonly int _descriptorSize;
    private readonly int _firstTextureSrvSlot;
    private readonly StaticSpriteShadowShaderConstantsUpdater _shaderConstants = new();
    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pipeline;

    public int DrawCallCount { get; private set; }

    public Dx12StaticSpriteShadowPass(
        ID3D12GraphicsCommandList commandList,
        GpuDescriptorHandle srvHeapGpuStart,
        int descriptorSize,
        int firstTextureSrvSlot)
    {
        _commandList = commandList;
        _srvHeapGpuStart = srvHeapGpuStart;
        _descriptorSize = descriptorSize;
        _firstTextureSrvSlot = firstTextureSrvSlot;
    }

    public void SetPipeline(ID3D12RootSignature rootSignature, ID3D12PipelineState pipeline)
    {
        _rootSignature = rootSignature;
        _pipeline = pipeline;
    }

    public void ClearPipeline()
    {
        _rootSignature = null;
        _pipeline = null;
    }

    public unsafe void Record(
        WorldSpriteBatch batch,
        SceneLighting lighting,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        DrawCallCount = 0;
        if (batch.ShadowInstanceCount == 0 ||
            MathF.Max(lighting.OutdoorShadowOpacity, lighting.IndoorShadowOpacity) <= 0.001f ||
            _rootSignature is null ||
            _pipeline is null)
        {
            return;
        }

        var constants = stackalloc float[StaticSpriteShadowSceneConstants.FloatCount];
        _shaderConstants.Write(
            constants,
            new StaticSpriteShadowSceneConstants(
                new Vector2(renderWidth, renderHeight),
                lighting.OutdoorShadowOpacity,
                lighting.IndoorShadowOpacity,
                DirectionalProjection,
                batch.ShadowAtlasTexelSize));

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _commandList.SetGraphicsRoot32BitConstants(
            StaticSpriteShaderLayout.SceneConstantsRootParameter,
            StaticSpriteShadowSceneConstants.FloatCount,
            constants,
            0);
        _commandList.SetGraphicsRootDescriptorTable(
            StaticSpriteShaderLayout.TextureTableRootParameter,
            SrvGpuHandle(_firstTextureSrvSlot + (int)batch.ShadowTextureSlot));
        _commandList.SetGraphicsRootShaderResourceView(
            StaticSpriteShaderLayout.InstanceBufferRootParameter,
            frame.StaticShadowInstanceBuffer.GPUVirtualAddress);
        _commandList.DrawInstanced(4, (uint)batch.ShadowInstanceCount, 0, 0);
        DrawCallCount = 1;
    }

    private GpuDescriptorHandle SrvGpuHandle(int index) =>
        _srvHeapGpuStart + index * _descriptorSize;

}
