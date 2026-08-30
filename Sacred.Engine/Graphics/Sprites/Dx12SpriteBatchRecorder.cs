using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Engine.Graphics.Frames;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Records texture-grouped liquid and static-sprite instances.</summary>
internal sealed class Dx12SpriteBatchRecorder
{
    private const float AlphaCutoff = 0.45f;
    private static readonly int InstanceStride = Marshal.SizeOf<StaticSpriteInstance>();

    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly GpuDescriptorHandle _srvHeapGpuStart;
    private readonly int _descriptorSize;
    private readonly int _firstTextureSrvSlot;
    private readonly StaticSpriteShaderConstantsUpdater _shaderConstants = new();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private ID3D12RootSignature? _rootSignature;

    public Dx12SpriteBatchRecorder(
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

    public void SetRootSignature(ID3D12RootSignature rootSignature) => _rootSignature = rootSignature;

    public void ClearRootSignature() => _rootSignature = null;

    public unsafe void Record(
        int startInstance,
        int instanceCount,
        ID3D12PipelineState? pipeline,
        Vector3 ambientColour,
        float paperWhiteNits,
        float unlitWhiteNits,
        int worldLightCount,
        float nightBlend,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        if (instanceCount == 0 || pipeline is null || _rootSignature is null)
            return;

        var sceneConstants = stackalloc float[StaticSpriteShaderLayout.SceneConstantsCount];
        _shaderConstants.Write(
            sceneConstants,
            new StaticSpriteSceneConstants(
                new Vector2(renderWidth, renderHeight),
                AlphaCutoff,
                ambientColour,
                paperWhiteNits,
                unlitWhiteNits,
                (float)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds,
                worldLightCount,
                nightBlend));

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(pipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.SetGraphicsRoot32BitConstants(
            StaticSpriteShaderLayout.SceneConstantsRootParameter,
            StaticSpriteShaderLayout.SceneConstantsCount,
            sceneConstants,
            0);
        _commandList.SetGraphicsRootShaderResourceView(
            StaticSpriteShaderLayout.WorldLightBufferRootParameter,
            frame.LightHaloInstanceBuffer.GPUVirtualAddress);
        var instances = (StaticSpriteInstance*)frame.SpriteInstanceBufferMapped + startInstance;
        var firstInstance = 0;
        while (firstInstance < instanceCount)
        {
            var textureSlot = instances[firstInstance].TextureIndex;
            var runLength = 1;
            while (firstInstance + runLength < instanceCount &&
                   instances[firstInstance + runLength].TextureIndex == textureSlot)
            {
                runLength++;
            }

            _commandList.SetGraphicsRootDescriptorTable(
                StaticSpriteShaderLayout.TextureTableRootParameter,
                SrvGpuHandle(_firstTextureSrvSlot + (int)textureSlot));
            _commandList.SetGraphicsRootShaderResourceView(
                StaticSpriteShaderLayout.InstanceBufferRootParameter,
                frame.SpriteInstanceBuffer.GPUVirtualAddress +
                (ulong)((startInstance + firstInstance) * InstanceStride));
            _commandList.DrawInstanced(6, (uint)runLength, 0, 0);
            firstInstance += runLength;
        }
    }

    private GpuDescriptorHandle SrvGpuHandle(int index) =>
        _srvHeapGpuStart + index * _descriptorSize;
}
