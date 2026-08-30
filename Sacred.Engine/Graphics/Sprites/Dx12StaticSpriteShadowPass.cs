using System;
using System.Numerics;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Records file-authored soft shadows beneath static world sprites.</summary>
internal sealed class Dx12StaticSpriteShadowPass
{
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
        SacredCamera camera,
        SceneLighting lighting,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        DrawCallCount = 0;
        if (batch.ShadowInstanceCount == 0 ||
            lighting.ShadowMode == SceneShadowMode.None ||
            lighting.DirectionToSun.Z <= 0.0f ||
            lighting.ShadowOpacity <= 0.001f ||
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
                lighting.ShadowOpacity,
                CalculateScreenProjection(camera, lighting.DirectionToSun, renderWidth, renderHeight),
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

    private static Vector2 CalculateScreenProjection(
        SacredCamera camera,
        Vector3 directionToSun,
        int renderWidth,
        int renderHeight)
    {
        var direction = directionToSun.LengthSquared() > float.Epsilon
            ? Vector3.Normalize(directionToSun)
            : Vector3.UnitZ;
        var vertical = MathF.Max(direction.Z, 0.18f);
        var worldProjection = new Vector4(
            -direction.X / vertical,
            -direction.Y / vertical,
            0.0f,
            0.0f);
        var clipProjection = Vector4.Transform(
            worldProjection,
            camera.View * camera.Projection);
        var screenProjection = new Vector2(
            clipProjection.X * renderWidth * 0.5f,
            -clipProjection.Y * renderHeight * 0.5f);
        if (screenProjection.LengthSquared() <= float.Epsilon)
            return new Vector2(1.0f, -1.0f);

        var solarSlope = MathF.Sqrt(
            direction.X * direction.X + direction.Y * direction.Y) / vertical;
        return Vector2.Normalize(screenProjection) * Math.Clamp(solarSlope, 0.45f, 1.75f);
    }
}
