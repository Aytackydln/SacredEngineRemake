using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Engine.Graphics.Frames;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Records painter-ordered sprite ranges with direct texture indexing.</summary>
internal sealed class Dx12SpriteBatchRecorder
{
    public float? AnimationTimeOverride { get; set; }
    private const float AlphaCutoff = 64.0f / 255.0f;
    private const float PlayerOccluderOpacity = 0.48f;
    private static readonly int InstanceStride = Marshal.SizeOf<StaticSpriteInstance>();

    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly int _firstTextureSrvSlot;
    private readonly GpuDescriptorHandle _surfaceLightMap;
    private readonly GpuDescriptorHandle _playerOcclusionMap;
    private readonly StaticSpriteShaderConstantsUpdater _shaderConstants = new();
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private ID3D12RootSignature? _rootSignature;

    public Dx12SpriteBatchRecorder(
        ID3D12GraphicsCommandList commandList,
        GpuDescriptorHandle srvHeapGpuStart,
        int descriptorSize,
        int firstTextureSrvSlot,
        GpuDescriptorHandle surfaceLightMap,
        GpuDescriptorHandle playerOcclusionMap)
    {
        _commandList = commandList;
        _firstTextureSrvSlot = firstTextureSrvSlot;
        _surfaceLightMap = surfaceLightMap;
        _playerOcclusionMap = playerOcclusionMap;
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
        PlayerOcclusionProbe playerOcclusion,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        if (startInstance < 0 || instanceCount <= 0 || pipeline is null || _rootSignature is null || frame.SpriteInstanceBufferMapped == 0)
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
                AnimationTimeOverride ?? (float)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds,
                PlayerOccluderOpacity,
                playerOcclusion.SceneDepth));

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(pipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.SetGraphicsRoot32BitConstants(
            StaticSpriteShaderLayout.SceneConstantsRootParameter,
            StaticSpriteShaderLayout.SceneConstantsCount,
            sceneConstants,
            0);
        _commandList.SetGraphicsRootDescriptorTable(
            StaticSpriteShaderLayout.SurfaceLightMapRootParameter,
            _surfaceLightMap);
        _commandList.SetGraphicsRootDescriptorTable(
            StaticSpriteShaderLayout.PlayerOcclusionMapRootParameter,
            _playerOcclusionMap);
        _commandList.SetGraphicsRoot32BitConstant(
            StaticSpriteShaderLayout.SceneConstantsRootParameter,
            (uint)_firstTextureSrvSlot,
            StaticSpriteShaderLayout.FirstTextureDescriptorConstantsOffset);
        _commandList.SetGraphicsRootShaderResourceView(
            StaticSpriteShaderLayout.InstanceBufferRootParameter,
            frame.SpriteInstanceBuffer.GPUVirtualAddress + (ulong)(startInstance * InstanceStride));
        _commandList.DrawInstanced(6, (uint)instanceCount, 0, 0);
    }

}
