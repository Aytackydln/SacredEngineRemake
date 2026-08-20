using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
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

    public int CandidateCount { get; private set; }
    public int InstanceCount { get; private set; }
    public int SurfaceLightCount { get; private set; }

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
        SceneLighting lighting,
        Vector3? playerLightWorldPosition,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight,
        ulong spriteRevision)
    {
        var state = _frameStates[frame.Index];
        var hasPlayerLight = playerLightWorldPosition.HasValue &&
                             lighting.PlayerLightDiameter > 0.0f &&
                             lighting.PlayerLightOpacity > 0.0f;
        CandidateCount = lights.Count + (hasPlayerLight ? 1 : 0);
        // Pixel shaders require a valid root SRV even when the authored scene has no lights.
        frame.EnsureLightHaloInstanceCapacity(_device, InstanceStride, Math.Max(1, CandidateCount));
        if (state.Matches(
                spriteRevision,
                camera.WorldCenter,
                camera.ViewportZoom,
                renderWidth,
                renderHeight,
                playerLightWorldPosition,
                lighting.PlayerLightDiameter,
                lighting.PlayerLightColour,
                lighting.PlayerLightOpacity))
        {
            InstanceCount = state.InstanceCount;
            SurfaceLightCount = state.SurfaceLightCount;
            return InstanceCount;
        }

        if (CandidateCount == 0)
        {
            state.Remember(
                spriteRevision,
                camera.WorldCenter,
                camera.ViewportZoom,
                renderWidth,
                renderHeight,
                playerLightWorldPosition,
                lighting.PlayerLightDiameter,
                lighting.PlayerLightColour,
                lighting.PlayerLightOpacity,
                instanceCount: 0,
                surfaceLightCount: 0);
            InstanceCount = 0;
            SurfaceLightCount = 0;
            return 0;
        }

        var screenTransform = IsometricProjection.CreateScreenTransform(
            camera.WorldCenter,
            camera.ViewportZoom,
            renderWidth,
            renderHeight);
        var instances = (LightHaloInstance*)frame.LightHaloInstanceBufferMapped;
        var instanceCount = 0;
        if (hasPlayerLight)
        {
            var diameter = screenTransform.Scale(lighting.PlayerLightDiameter);
            var playerScreenPosition = ProjectWorldToScreen(
                playerLightWorldPosition!.Value,
                camera,
                renderWidth,
                renderHeight);
            instances[instanceCount++] = new LightHaloInstance(
                playerScreenPosition.X - diameter * 0.5f,
                playerScreenPosition.Y - diameter * 0.5f,
                diameter,
                lighting.PlayerLightOpacity,
                lighting.PlayerLightColour,
                (uint)WorldLightShape.SurfaceIllumination);
        }

        for (var phase = 0; phase < 2; phase++)
        {
            var surfacePhase = phase == 0;
            for (var index = 0; index < lights.Count; index++)
            {
                var light = lights[index];
                if ((light.Shape == WorldLightShape.SurfaceIllumination) != surfacePhase)
                    continue;

                var drawPosition = screenTransform.ToScreen(light.IsoX, light.IsoY);
                var diameter = screenTransform.Scale(light.Diameter);
                if (drawPosition.X >= renderWidth || drawPosition.Y >= renderHeight ||
                    drawPosition.X + diameter <= 0.0f || drawPosition.Y + diameter <= 0.0f)
                {
                    continue;
                }

                instances[instanceCount++] = new LightHaloInstance(
                    drawPosition.X,
                    drawPosition.Y,
                    diameter,
                    light.Opacity,
                    light.Colour,
                    (uint)light.Shape);
            }

            if (surfacePhase)
                SurfaceLightCount = instanceCount;
        }

        state.Remember(
            spriteRevision,
            camera.WorldCenter,
            screenTransform.Zoom,
            renderWidth,
            renderHeight,
            playerLightWorldPosition,
            lighting.PlayerLightDiameter,
            lighting.PlayerLightColour,
            lighting.PlayerLightOpacity,
            instanceCount,
            SurfaceLightCount);
        InstanceCount = instanceCount;
        return instanceCount;
    }

    private static Vector2 ProjectWorldToScreen(
        Vector3 worldPosition,
        SacredCamera camera,
        int renderWidth,
        int renderHeight)
    {
        var clip = Vector4.Transform(new Vector4(worldPosition, 1.0f), camera.View * camera.Projection);
        var inverseW = MathF.Abs(clip.W) > float.Epsilon ? 1.0f / clip.W : 1.0f;
        return new Vector2(
            (clip.X * inverseW * 0.5f + 0.5f) * renderWidth,
            (0.5f - clip.Y * inverseW * 0.5f) * renderHeight);
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
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleStrip);
        _commandList.SetGraphicsRoot32BitConstants(
            LightHaloShaderLayout.SceneConstantsRootParameter,
            LightHaloShaderLayout.SceneConstantsCount,
            sceneConstants,
            0);
        _commandList.SetGraphicsRootShaderResourceView(
            LightHaloShaderLayout.InstanceBufferRootParameter,
            frame.LightHaloInstanceBuffer.GPUVirtualAddress);
        _commandList.DrawInstanced(4, (uint)instanceCount, 0, 0);
    }

    private sealed class LightHaloFrameState
    {
        private ulong _spriteRevision;
        private Vector2 _worldCenter;
        private float _viewportZoom;
        private int _renderWidth;
        private int _renderHeight;
        private Vector3? _playerLightWorldPosition;
        private float _playerLightDiameter;
        private Vector3 _playerLightColour;
        private float _playerLightOpacity;
        private bool _valid;

        public int InstanceCount { get; private set; }
        public int SurfaceLightCount { get; private set; }

        public bool Matches(
            ulong spriteRevision,
            Vector2 worldCenter,
            float viewportZoom,
            int renderWidth,
            int renderHeight,
            Vector3? playerLightWorldPosition,
            float playerLightDiameter,
            Vector3 playerLightColour,
            float playerLightOpacity) =>
            _valid &&
            _spriteRevision == spriteRevision &&
            _worldCenter == worldCenter &&
            _viewportZoom == viewportZoom &&
            _renderWidth == renderWidth &&
            _renderHeight == renderHeight &&
            _playerLightWorldPosition == playerLightWorldPosition &&
            _playerLightDiameter == playerLightDiameter &&
            _playerLightColour == playerLightColour &&
            _playerLightOpacity == playerLightOpacity;

        public void Remember(
            ulong spriteRevision,
            Vector2 worldCenter,
            float viewportZoom,
            int renderWidth,
            int renderHeight,
            Vector3? playerLightWorldPosition,
            float playerLightDiameter,
            Vector3 playerLightColour,
            float playerLightOpacity,
            int instanceCount,
            int surfaceLightCount)
        {
            _spriteRevision = spriteRevision;
            _worldCenter = worldCenter;
            _viewportZoom = viewportZoom;
            _renderWidth = renderWidth;
            _renderHeight = renderHeight;
            _playerLightWorldPosition = playerLightWorldPosition;
            _playerLightDiameter = playerLightDiameter;
            _playerLightColour = playerLightColour;
            _playerLightOpacity = playerLightOpacity;
            InstanceCount = instanceCount;
            SurfaceLightCount = surfaceLightCount;
            _valid = true;
        }
    }
}
