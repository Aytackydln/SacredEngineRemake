using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Geometry;
using Vortice.Direct3D;
using Vortice.Direct3D12;

namespace Sacred.Engine.Graphics.Sprites;

/// <summary>Records texture-free, procedural world-light halos.</summary>
internal sealed class Dx12LightHaloPass
{
    private static readonly int InstanceStride = Marshal.SizeOf<LightHaloInstance>();

    private readonly ID3D12Device _device;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly LightHaloFrameState[] _frameStates;
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pipeline;

    public Dx12LightHaloPass(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        int frameCount)
    {
        _device = device;
        _commandList = commandList;
        _frameStates = new LightHaloFrameState[frameCount];
        for (var index = 0; index < frameCount; index++)
            _frameStates[index] = new LightHaloFrameState();
    }

    public void SetPipeline(Dx12CreatedPipelineGroup pipeline)
    {
        _rootSignature = pipeline.RootSignature;
        _pipeline = pipeline[Dx12PipelineKind.LightHalo];
    }

    public void DisposePipeline()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        _rootSignature?.Dispose();
        _rootSignature = null;
    }

    public unsafe int PrepareInstances(
        SacredCamera camera,
        IReadOnlyList<TerrainWorldLight> lights,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight,
        ulong spriteRevision)
    {
        var state = _frameStates[frame.Index];
        if (state.Matches(spriteRevision, camera.WorldCenter, camera.ViewportZoom, renderWidth, renderHeight))
            return state.InstanceCount;

        if (lights.Count == 0)
        {
            state.Remember(
                spriteRevision,
                camera.WorldCenter,
                camera.ViewportZoom,
                renderWidth,
                renderHeight,
                0);
            return 0;
        }

        var screenTransform = IsometricProjection.CreateScreenTransform(
            camera.WorldCenter,
            camera.ViewportZoom,
            renderWidth,
            renderHeight);
        frame.EnsureLightHaloInstanceCapacity(_device, InstanceStride, lights.Count);
        var instances = (LightHaloInstance*)frame.LightHaloInstanceBufferMapped;
        for (var index = 0; index < lights.Count; index++)
        {
            var light = lights[index];
            var drawPosition = screenTransform.ToScreen(light.IsoX, light.IsoY);
            instances[index] = new LightHaloInstance(
                drawPosition.X,
                drawPosition.Y,
                screenTransform.Scale(light.Diameter),
                light.Opacity,
                light.Colour,
                (uint)light.Shape);
        }

        state.Remember(
            spriteRevision,
            camera.WorldCenter,
            screenTransform.Zoom,
            renderWidth,
            renderHeight,
            lights.Count);
        return lights.Count;
    }

    public unsafe void Record(
        int instanceCount,
        float nightBlend,
        float whiteNits,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        if (instanceCount == 0 || _pipeline is null || _rootSignature is null)
            return;

        var sceneConstants = stackalloc float[LightHaloShaderLayout.SceneConstantsCount];
        LightHaloShaderConstantsWriter.Write(
            sceneConstants,
            new LightHaloSceneConstants(
                new Vector2(renderWidth, renderHeight),
                nightBlend,
                whiteNits,
                (float)Stopwatch.GetElapsedTime(_startTimestamp).TotalSeconds));

        _commandList.SetGraphicsRootSignature(_rootSignature);
        _commandList.SetPipelineState(_pipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.SetGraphicsRoot32BitConstants(
            LightHaloShaderLayout.SceneConstantsRootParameter,
            LightHaloShaderLayout.SceneConstantsCount,
            sceneConstants,
            0);
        _commandList.SetGraphicsRootShaderResourceView(
            LightHaloShaderLayout.InstanceBufferRootParameter,
            frame.LightHaloInstanceBuffer.GPUVirtualAddress);
        _commandList.DrawInstanced(6, (uint)instanceCount, 0, 0);
    }

    private sealed class LightHaloFrameState
    {
        private ulong _spriteRevision;
        private Vector2 _worldCenter;
        private float _viewportZoom;
        private int _renderWidth;
        private int _renderHeight;
        private bool _valid;

        public int InstanceCount { get; private set; }

        public bool Matches(
            ulong spriteRevision,
            Vector2 worldCenter,
            float viewportZoom,
            int renderWidth,
            int renderHeight) =>
            _valid &&
            _spriteRevision == spriteRevision &&
            _worldCenter == worldCenter &&
            _viewportZoom == viewportZoom &&
            _renderWidth == renderWidth &&
            _renderHeight == renderHeight;

        public void Remember(
            ulong spriteRevision,
            Vector2 worldCenter,
            float viewportZoom,
            int renderWidth,
            int renderHeight,
            int instanceCount)
        {
            _spriteRevision = spriteRevision;
            _worldCenter = worldCenter;
            _viewportZoom = viewportZoom;
            _renderWidth = renderWidth;
            _renderHeight = renderHeight;
            InstanceCount = instanceCount;
            _valid = true;
        }
    }
}
