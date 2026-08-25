using System;
using System.Collections.Generic;
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

/// <summary>Records texture-backed world-light halos and surface-light instances.</summary>
internal sealed class Dx12LightHaloPass : IDisposable
{
    private static readonly int InstanceStride = Marshal.SizeOf<LightHaloInstance>();
    // Ground and static sprites evaluate every surface light per covered pixel.
    // This matches the shader's fixed maximum. A smaller selection made ordinary
    // lamp clusters switch light volumes while the camera moved.
    private const int MaximumSurfaceIlluminationLights = 64;

    private readonly ID3D12Device _device;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly Dx12TextureUploader _textureUploader;
    private readonly CpuDescriptorHandle _haloTextureCpuHandle;
    private readonly GpuDescriptorHandle _haloTextureGpuHandle;
    private readonly LightHaloFrameState[] _frameStates;

    private ID3D12RootSignature? _rootSignature;
    private ID3D12PipelineState? _pipeline;
    private ID3D12Resource? _haloTexture;

    public int CandidateCount { get; private set; }
    public int InstanceCount { get; private set; }
    public int SurfaceLightCount { get; private set; }

    public Dx12LightHaloPass(
        ID3D12Device device,
        ID3D12GraphicsCommandList commandList,
        Dx12TextureUploader textureUploader,
        CpuDescriptorHandle haloTextureCpuHandle,
        GpuDescriptorHandle haloTextureGpuHandle,
        int frameCount)
    {
        _device = device;
        _commandList = commandList;
        _textureUploader = textureUploader;
        _haloTextureCpuHandle = haloTextureCpuHandle;
        _haloTextureGpuHandle = haloTextureGpuHandle;
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

    public void PrepareTexture(
        IReadOnlyList<TerrainWorldLight> lights,
        Dx12FrameContext frame)
    {
        if (_haloTexture is not null)
            return;

        for (var index = 0; index < lights.Count; index++)
        {
            var mask = lights[index].Mask;
            if (mask is null)
                continue;

            var rgba = mask.Rgba;
            if (rgba.Length == 0)
                return;

            _haloTexture = _textureUploader.UploadRgbaTexture(
                _commandList,
                mask.AtlasWidth,
                mask.AtlasHeight,
                rgba,
                frame.TransientResources);
            _textureUploader.CreateShaderResourceView(_haloTexture, _haloTextureCpuHandle);
            mask.ReleasePixelData();
            EngineLog.WriteLine($"Light halo texture uploaded: {mask.Width}x{mask.Height}.");
            return;
        }
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

        // Only surface illumination enters the terrain/static-sprite shader's
        // per-pixel loop. Keep every visible source up to the shader maximum;
        // the player lamp occupies a protected first slot.
        var surfaceBudget = MaximumSurfaceIlluminationLights - instanceCount;
        var visibleSurfaceLights = new List<VisibleSurfaceLight>(Math.Min(lights.Count, surfaceBudget));
        for (var index = 0; index < lights.Count; index++)
        {
            var light = lights[index];
            if (light.Shape != WorldLightShape.SurfaceIllumination || light.Diameter <= 0.0f)
                continue;

            var drawPosition = screenTransform.ToScreen(light.IsoX, light.IsoY);
            var diameter = screenTransform.Scale(light.Diameter);
            if (!IntersectsViewport(drawPosition, diameter, renderWidth, renderHeight))
                continue;

            visibleSurfaceLights.Add(new VisibleSurfaceLight(light, drawPosition, diameter,
                CalculateSurfacePriority(drawPosition, diameter, light.Opacity, renderWidth, renderHeight)));
        }

        visibleSurfaceLights.Sort(static (left, right) => right.Priority.CompareTo(left.Priority));
        var selectedSurfaceLightCount = Math.Min(visibleSurfaceLights.Count, Math.Max(0, surfaceBudget));
        for (var index = 0; index < selectedSurfaceLightCount; index++)
        {
            var visibleLight = visibleSurfaceLights[index];
            var light = visibleLight.Light;
            instances[instanceCount++] = new LightHaloInstance(
                visibleLight.DrawPosition.X,
                visibleLight.DrawPosition.Y,
                visibleLight.Diameter,
                light.Opacity,
                light.Colour,
                (uint)light.Shape);
        }

        SurfaceLightCount = instanceCount;

        // Visible halo effects do not take part in terrain lighting, so they
        // retain their complete authored set without increasing shader cost.
        for (var index = 0; index < lights.Count; index++)
        {
            var light = lights[index];
            if (light.Shape == WorldLightShape.SurfaceIllumination)
                continue;

            var drawPosition = screenTransform.ToScreen(light.IsoX, light.IsoY);
            var diameter = screenTransform.Scale(light.Diameter);
            if (!IntersectsViewport(drawPosition, diameter, renderWidth, renderHeight))
                continue;

            instances[instanceCount++] = new LightHaloInstance(
                drawPosition.X, drawPosition.Y, diameter, light.Opacity, light.Colour, (uint)light.Shape);
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

    private static bool IntersectsViewport(Vector2 drawPosition, float diameter, int renderWidth, int renderHeight) =>
        drawPosition.X < renderWidth && drawPosition.Y < renderHeight &&
        drawPosition.X + diameter > 0.0f && drawPosition.Y + diameter > 0.0f;

    private static float CalculateSurfacePriority(
        Vector2 drawPosition, float diameter, float opacity, int renderWidth, int renderHeight)
    {
        var lightCenter = drawPosition + new Vector2(diameter * 0.5f);
        var viewportCenter = new Vector2(renderWidth * 0.5f, renderHeight * 0.5f);
        var distance = Vector2.Distance(lightCenter, viewportCenter);
        // A large/bright volume is useful over a broader part of the current
        // view; nearby volumes win ties, avoiding obvious illumination pops.
        return MathF.Max(0.0f, opacity) * diameter / (1.0f + distance / MathF.Max(1.0f, diameter));
    }

    private readonly record struct VisibleSurfaceLight(
        TerrainWorldLight Light,
        Vector2 DrawPosition,
        float Diameter,
        float Priority);

    public unsafe void Record(
        int instanceCount,
        float nightBlend,
        float whiteNits,
        Dx12FrameContext frame,
        int renderWidth,
        int renderHeight)
    {
        var visibleEffectCount = instanceCount - SurfaceLightCount;
        if (visibleEffectCount == 0 || _haloTexture is null ||
            _pipeline is null || _rootSignature is null)
            return;

        var sceneConstants = stackalloc float[LightHaloShaderLayout.SceneConstantsCount];
        LightHaloShaderConstantsWriter.Write(
            sceneConstants,
            new LightHaloSceneConstants(
                new Vector2(renderWidth, renderHeight),
                nightBlend,
                whiteNits));

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
            frame.LightHaloInstanceBuffer.GPUVirtualAddress +
            (ulong)(SurfaceLightCount * InstanceStride));
        _commandList.SetGraphicsRootDescriptorTable(
            LightHaloShaderLayout.TextureTableRootParameter,
            _haloTextureGpuHandle);
        // Bind the visible-effect subrange explicitly. SV_InstanceID starts at
        // zero for this shader-only instancing path; StartInstanceLocation is
        // intended for input-layout instance data and left the shader reading
        // the leading surface-light records (which it correctly discarded).
        _commandList.DrawInstanced(4, (uint)visibleEffectCount, 0, 0);
    }

    public void Dispose()
    {
        _haloTexture?.Dispose();
        _haloTexture = null;
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
