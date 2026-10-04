using System;
using System.Collections.Generic;
using System.Numerics;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Graphics.Lighting;
using Sacred.Engine.Graphics.Minimap;
using Sacred.Engine.Graphics.Models;
using Sacred.Engine.Graphics.Particles;
using Sacred.Engine.Graphics.Sprites;
using Sacred.Engine.Graphics.Swapchain;
using Sacred.Engine.Graphics.Terrain;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Geometry;
using Vortice;
using Vortice.Direct3D;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

/// <summary>Records the ordered world-rendering passes into one Direct3D 12 command list.</summary>
internal sealed class Dx12WorldCommandRecorder
{
    public Dx12GpuParticlePass? GpuParticles { get; set; }
    private const float MinimumHighlightNits = 1000.0f;
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly GpuDescriptorHandle _srvHeapStart;
    private readonly int _srvDescriptorSize;
    private readonly Dx12SectorTextureCache _sectorTextures;
    private readonly Dx12TerrainDebugPass _terrainDebug;
    private readonly Dx12SpritePass _sprites;
    private readonly Dx12SurfaceLightMapPass _surfaceLights;
    private readonly Dx12ShadowMap _shadowMap;
    private readonly Dx12PlayerOcclusionMapPass _playerOcclusionMap;
    private readonly Dx12LightHaloPass _lightHalos;
    private readonly List<TerrainWorldLight> _frameWorldLights = new(65);
    private readonly Dx12ModelPass _models;
    private readonly Dx12WorldPainterPass _worldPainter;
    private readonly Dx12DebugOverlay _debugOverlay;
    private readonly Dx12ImGuiRenderer _imgui;
    private readonly Dx12MinimapPass _minimap;
    private readonly WorldQuadShaderConstantsUpdater _worldQuadConstants = new();

    public Dx12WorldCommandRecorder(
        ID3D12GraphicsCommandList commandList,
        ID3D12DescriptorHeap srvHeap,
        int srvDescriptorSize,
        Dx12SectorTextureCache sectorTextures,
        Dx12TerrainDebugPass terrainDebug,
        Dx12SpritePass sprites,
        Dx12SurfaceLightMapPass surfaceLights,
        Dx12ShadowMap shadowMap,
        Dx12PlayerOcclusionMapPass playerOcclusionMap,
        Dx12LightHaloPass lightHalos,
        Dx12ModelPass models,
        Dx12DebugOverlay debugOverlay,
        Dx12ImGuiRenderer imgui,
        Dx12MinimapPass minimap)
    {
        _commandList = commandList;
        _srvHeapStart = srvHeap.GetGPUDescriptorHandleForHeapStart();
        _srvDescriptorSize = srvDescriptorSize;
        _sectorTextures = sectorTextures;
        _terrainDebug = terrainDebug;
        _sprites = sprites;
        _surfaceLights = surfaceLights;
        _shadowMap = shadowMap;
        _playerOcclusionMap = playerOcclusionMap;
        _lightHalos = lightHalos;
        _models = models;
        _worldPainter = new Dx12WorldPainterPass(sprites, models);
        _debugOverlay = debugOverlay;
        _imgui = imgui;
        _minimap = minimap;
    }

    public unsafe void Record(
        SacredCamera camera,
        IReadOnlyList<TerrainSectorComposition> sectorImages,
        IReadOnlyList<TerrainLiquidSprite> liquidSprites,
        IReadOnlyList<TerrainStaticSprite> staticSprites,
        IReadOnlyList<TerrainWorldLight> worldLights,
        SceneState scene,
        ulong worldSpriteRevision,
        Dx12FrameContext frame,
        ID3D12Resource sceneColor,
        CpuDescriptorHandle renderTarget,
        ResourceStates initialColorState,
        ResourceStates finalColorState,
        CpuDescriptorHandle depthStencil,
        ID3D12DescriptorHeap[] shaderVisibleDescriptorHeaps,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState terrainPipeline,
        ID3D12PipelineState liquidCoverPipeline,
        ID3D12PipelineState shadowOverlayPipeline,
        Dx12SceneColorProfile displayProfile,
        int renderWidth,
        int renderHeight)
    {
        Dx12TextureUploader.Transition(
            _commandList,
            sceneColor,
            initialColorState,
            ResourceStates.RenderTarget);

        _commandList.RSSetViewports(new Viewport(0, 0, renderWidth, renderHeight, 0.0f, 1.0f));
        _commandList.RSSetScissorRects(new RawRect(0, 0, renderWidth, renderHeight));
        _commandList.OMSetRenderTargets(renderTarget, null);
        _commandList.ClearRenderTargetView(renderTarget, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
        _commandList.SetDescriptorHeaps(1, shaderVisibleDescriptorHeaps);

        var spriteBatch = _sprites.PrepareInstances(
            camera,
            scene.Models.Count > 0 ? scene.Models[0] : null,
            liquidSprites,
            staticSprites,
            scene.Debug.HoveredStaticObjectId,
            frame,
            renderWidth,
            renderHeight,
            worldSpriteRevision);
        var frameWorldLights = PrepareFrameWorldLights(worldLights, camera, scene.Lighting);
        var lightHaloInstanceCount = _lightHalos.PrepareInstances(
            camera,
            frameWorldLights,
            scene.Lighting,
            playerLightWorldPosition: null,
            frame,
            renderWidth,
            renderHeight,
            worldSpriteRevision);
        _sprites.SetParticleColorMultiplier(displayProfile.ParticleColorMultiplier);
        var surfaceLightCount = _lightHalos.SurfaceLightCount;
        _surfaceLights.Record(
            surfaceLightCount,
            scene.Lighting.NightBlend,
            frame,
            renderWidth,
            renderHeight);
        _playerOcclusionMap.Record(spriteBatch.PlayerOcclusion, renderWidth, renderHeight);
        _commandList.OMSetRenderTargets(renderTarget, null);

        // Terrain and sprites use this exact transform for the whole frame. Independently
        // deriving it per pass opens moving seams at float rounding boundaries.
        var screenTransform = IsometricProjection.CreateScreenTransform(
            camera.WorldCenter,
            camera.ViewportZoom,
            renderWidth,
            renderHeight);

        var constants = stackalloc float[WorldQuadShaderLayout.RootConstantsCount];
        foreach (var image in sectorImages)
        {
            if (!_sectorTextures.TryGet(image.Coord, out var texture))
                continue;

            var drawPosition = screenTransform.ToScreen(image.IsoX, image.IsoY);
            var drawWidth = screenTransform.Scale(image.Width);
            var drawHeight = screenTransform.Scale(image.Height);
            RecordTerrainLayer(
                texture.BaseSrvSlot,
                drawPosition.X,
                drawPosition.Y,
                drawWidth,
                drawHeight,
                scene.Lighting.WorldSurfaceAmbientColour,
                false,
                constants,
                rootSignature,
                terrainPipeline,
                liquidCoverPipeline,
                displayProfile.SceneWhiteScale,
                renderWidth,
                renderHeight);

            if (_sprites.TryGetLiquidRange(image.Coord, out var liquidRange))
            {
                _sprites.RecordLiquid(
                    liquidRange,
                    scene.Lighting.WorldSurfaceAmbientColour,
                    displayProfile.SceneWhiteScale,
                    frame,
                    renderWidth,
                    renderHeight);
            }

            if (texture.HasLiquidCover)
            {
                RecordTerrainLayer(
                    texture.LiquidCoverSrvSlot,
                    drawPosition.X,
                    drawPosition.Y,
                    drawWidth,
                    drawHeight,
                    scene.Lighting.WorldSurfaceAmbientColour,
                    true,
                    constants,
                    rootSignature,
                    terrainPipeline,
                    liquidCoverPipeline,
                    displayProfile.SceneWhiteScale,
                    renderWidth,
                    renderHeight);
            }

            if (scene.Debug.BlockedAreasVisible && image.HasBlockedAreaDebugData)
            {
                _terrainDebug.Record(_commandList, image, image.BlockedAreaDebugTiles,
                    image.BlockedAreaDebugOffsetX, image.BlockedAreaDebugOffsetY,
                    screenTransform, frame, sceneColor.Description.Format,
                    displayProfile.SceneWhiteScale, renderWidth, renderHeight);
                _commandList.SetDescriptorHeaps(1, shaderVisibleDescriptorHeaps);
            }
        }

        // Native Floor sprites precede the Shadows queue; Floor2 and Objects follow it.
        _commandList.OMSetRenderTargets(renderTarget, depthStencil);
        _commandList.ClearDepthStencilView(depthStencil, ClearFlags.Depth, 1.0f, 0, 0, []);
        _worldPainter.RecordFloor(spriteBatch, camera, scene, displayProfile, frame, renderWidth, renderHeight);

        _shadowMap.Begin(renderWidth, renderHeight);
        _sprites.RecordStaticShadows(
            spriteBatch,
            camera,
            scene.Lighting,
            frame,
            renderWidth,
            renderHeight);
        _models.RecordShadows(camera, scene.Models, scene.Lighting, frame.Index);
        _shadowMap.End();
        _commandList.OMSetRenderTargets(renderTarget, null);
        RecordShadowOverlay(
            rootSignature,
            shadowOverlayPipeline,
            constants,
            renderWidth,
            renderHeight);
        _commandList.OMSetRenderTargets(renderTarget, depthStencil);
        _commandList.ClearDepthStencilView(depthStencil, ClearFlags.Depth, 1.0f, 0, 0, []);
        // Sprites and models share the native tile traversal below. Ordinary
        // scenery paints in that order without changing the model depth buffer.
        if (scene.Debug.StairsMapVisible)
        {
            _commandList.OMSetRenderTargets(renderTarget, null);
            _terrainDebug.RecordSectors(_commandList, sectorImages, _sectorTextures, TerrainDebugLayer.Stairs,
                screenTransform, frame, sceneColor.Description.Format,
                displayProfile.SceneWhiteScale, renderWidth, renderHeight);
            _commandList.SetDescriptorHeaps(1, shaderVisibleDescriptorHeaps);
            _commandList.OMSetRenderTargets(renderTarget, depthStencil);
            _commandList.ClearDepthStencilView(depthStencil, ClearFlags.Depth, 1.0f, 0, 0, []);
        }

        if (scene.Debug.TerrainTopologyVisible)
        {
            _commandList.OMSetRenderTargets(renderTarget, null);
            _terrainDebug.RecordSectors(_commandList, sectorImages, _sectorTextures, TerrainDebugLayer.Topology,
                screenTransform, frame, sceneColor.Description.Format,
                displayProfile.SceneWhiteScale, renderWidth, renderHeight);
            _commandList.SetDescriptorHeaps(1, shaderVisibleDescriptorHeaps);
            _commandList.OMSetRenderTargets(renderTarget, depthStencil);
            _commandList.ClearDepthStencilView(depthStencil, ClearFlags.Depth, 1.0f, 0, 0, []);
        }

        _worldPainter.Record(spriteBatch, camera, scene, displayProfile, frame, renderWidth, renderHeight);
        _sprites.RecordTransparentStatic(
            spriteBatch,
            scene.Lighting.WorldSurfaceAmbientColour,
            displayProfile.SceneWhiteScale,
            displayProfile.UnlitSpriteScale,
            frame,
            renderWidth,
            renderHeight);

        // GPU particles use the same depth-tested pipeline as CPU particle sprites.
        _commandList.OMSetRenderTargets(renderTarget, depthStencil);
        GpuParticles?.Draw(_commandList,displayProfile,renderWidth,renderHeight,_models.GpuTimings!);

        // Light halos are screen-space overlays in Sacred and must remain above depth-tested art.
        _commandList.OMSetRenderTargets(renderTarget, null);
        _lightHalos.Record(
            lightHaloInstanceCount,
            scene.Lighting.NightBlend,
            displayProfile.ParticleColorMultiplier,
            frame,
            renderWidth,
            renderHeight);

        if (spriteBatch.HighlightedStaticInstance is not null)
        {
            _commandList.SetGraphicsRootSignature(rootSignature);
            _commandList.SetPipelineState(liquidCoverPipeline);
            _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
            _commandList.SetGraphicsRootDescriptorTable(
                WorldQuadShaderLayout.SurfaceLightMapRootParameter,
                _surfaceLights.ShaderResourceHandle);
            _debugOverlay.RecordSceneDim(
                renderWidth,
                renderHeight,
                displayProfile.SceneWhiteScale);

            _commandList.OMSetRenderTargets(renderTarget, depthStencil);
            var highlightNits = MathF.Max(
                MinimumHighlightNits,
                displayProfile.UnlitSpriteScale * 1.5f);
            _sprites.RecordHighlightedStatic(
                spriteBatch,
                highlightNits,
                frame,
                renderWidth,
                renderHeight);
        }

        Dx12TextureUploader.Transition(
            _commandList,
            sceneColor,
            ResourceStates.RenderTarget,
            finalColorState);
    }

    private unsafe void RecordShadowOverlay(
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipeline,
        float* constants,
        int renderWidth,
        int renderHeight)
    {
        _worldQuadConstants.Write(
            constants,
            new WorldQuadShaderConstants(
                new Vector4(0.0f, 0.0f, renderWidth, renderHeight),
                new Vector2(renderWidth, renderHeight),
                Vector3.One,
                false,
                0.0f));
        _commandList.SetGraphicsRootSignature(rootSignature);
        _commandList.SetPipelineState(pipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _commandList.SetGraphicsRoot32BitConstants(
            WorldQuadShaderLayout.RootConstantsRootParameter,
            WorldQuadShaderLayout.RootConstantsCount,
            constants,
            0);
        _commandList.SetGraphicsRootDescriptorTable(
            WorldQuadShaderLayout.TextureRootParameter,
            _shadowMap.ShaderResourceHandle);
        _commandList.DrawInstanced(6, 1, 0, 0);
    }

    private IReadOnlyList<TerrainWorldLight> PrepareFrameWorldLights(
        IReadOnlyList<TerrainWorldLight> worldLights,
        SacredCamera camera,
        SceneLighting lighting)
    {
        _frameWorldLights.Clear();
        _frameWorldLights.AddRange(worldLights);
        if (lighting.PlayerLightDiameter <= 0.0f || lighting.PlayerLightOpacity <= 0.0f)
            return _frameWorldLights;

        var radius = lighting.PlayerLightDiameter * 0.5f;
        var playerIsoPosition = IsometricProjection.WorldToIso(camera.WorldCenter);
        _frameWorldLights.Add(new TerrainWorldLight(
            playerIsoPosition.X - radius,
            playerIsoPosition.Y - radius,
            lighting.PlayerLightDiameter,
            lighting.PlayerLightColour,
            lighting.PlayerLightOpacity,
            WorldLightShape.SurfaceIllumination));
        return _frameWorldLights;
    }

    public void RecordUi(
        SceneState scene,
        Dx12FrameContext frame,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState terrainPipeline,
        Dx12SceneColorProfile displayProfile,
        int outputWidth,
        int outputHeight)
    {
        _commandList.SetGraphicsRootSignature(rootSignature);
        _commandList.SetPipelineState(terrainPipeline);
        _commandList.SetGraphicsRootDescriptorTable(
            WorldQuadShaderLayout.SurfaceLightMapRootParameter,
            _surfaceLights.ShaderResourceHandle);
        if (scene.Debug.OverlaysVisible)
            _debugOverlay.RecordDebugOverlay(outputWidth, outputHeight, displayProfile.UiWhiteScale);
        if (scene.Minimap.IsVisible)
            _minimap.Record(
                rootSignature,
                terrainPipeline,
                outputWidth,
                outputHeight,
                displayProfile.UiWhiteScale);
        _imgui.Record(frame, displayProfile.UiWhiteScale);
    }

    private unsafe void RecordTerrainLayer(
        int srvSlot,
        float drawX,
        float drawY,
        float drawWidth,
        float drawHeight,
        Vector3 ambientColour,
        bool premultipliedAlpha,
        float* constants,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState terrainPipeline,
        ID3D12PipelineState liquidCoverPipeline,
        float paperWhiteNits,
        int renderWidth,
        int renderHeight)
    {
        _commandList.SetGraphicsRootSignature(rootSignature);
        _commandList.SetPipelineState(premultipliedAlpha ? liquidCoverPipeline : terrainPipeline);
        _commandList.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _worldQuadConstants.Write(
            constants,
            new WorldQuadShaderConstants(
                new Vector4(drawX, drawY, drawWidth, drawHeight),
                new Vector2(renderWidth, renderHeight),
                ambientColour,
                premultipliedAlpha,
                paperWhiteNits));
        _commandList.SetGraphicsRoot32BitConstants(
            WorldQuadShaderLayout.RootConstantsRootParameter,
            WorldQuadShaderLayout.RootConstantsCount,
            constants,
            0);
        _commandList.SetGraphicsRootDescriptorTable(
            WorldQuadShaderLayout.TextureRootParameter,
            _srvHeapStart + srvSlot * _srvDescriptorSize);
        _commandList.SetGraphicsRootDescriptorTable(
            WorldQuadShaderLayout.SurfaceLightMapRootParameter,
            _surfaceLights.ShaderResourceHandle);
        _commandList.DrawInstanced(6, 1, 0, 0);
    }
}
