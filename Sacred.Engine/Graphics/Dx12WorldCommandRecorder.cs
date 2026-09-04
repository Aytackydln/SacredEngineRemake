using System.Collections.Generic;
using System.Numerics;
using Sacred.Engine.Graphics.Frames;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Graphics.Minimap;
using Sacred.Engine.Graphics.Models;
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
    private readonly ID3D12GraphicsCommandList _commandList;
    private readonly GpuDescriptorHandle _srvHeapStart;
    private readonly int _srvDescriptorSize;
    private readonly Dx12SectorTextureCache _sectorTextures;
    private readonly Dx12SpritePass _sprites;
    private readonly Dx12LightHaloPass _lightHalos;
    private readonly Dx12ModelPass _models;
    private readonly Dx12DebugOverlay _debugOverlay;
    private readonly Dx12ImGuiRenderer _imgui;
    private readonly Dx12MinimapPass _minimap;
    private readonly WorldQuadShaderConstantsUpdater _worldQuadConstants = new();

    public Dx12WorldCommandRecorder(
        ID3D12GraphicsCommandList commandList,
        ID3D12DescriptorHeap srvHeap,
        int srvDescriptorSize,
        Dx12SectorTextureCache sectorTextures,
        Dx12SpritePass sprites,
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
        _sprites = sprites;
        _lightHalos = lightHalos;
        _models = models;
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
        ID3D12Resource backBuffer,
        CpuDescriptorHandle renderTarget,
        CpuDescriptorHandle depthStencil,
        ID3D12DescriptorHeap[] shaderVisibleDescriptorHeaps,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState terrainPipeline,
        ID3D12PipelineState liquidCoverPipeline,
        Dx12DisplayProfile displayProfile,
        int renderWidth,
        int renderHeight)
    {
        Dx12TextureUploader.Transition(
            _commandList,
            backBuffer,
            ResourceStates.Present,
            ResourceStates.RenderTarget);

        _commandList.RSSetViewports(new Viewport(0, 0, renderWidth, renderHeight, 0.0f, 1.0f));
        _commandList.RSSetScissorRects(new RawRect(0, 0, renderWidth, renderHeight));
        _commandList.OMSetRenderTargets(renderTarget, null);
        _commandList.ClearRenderTargetView(renderTarget, new Color4(0.0f, 0.0f, 0.0f, 1.0f));
        _commandList.SetDescriptorHeaps(1, shaderVisibleDescriptorHeaps);

        var spriteBatch = _sprites.PrepareInstances(
            camera,
            liquidSprites,
            staticSprites,
            frame,
            renderWidth,
            renderHeight,
            worldSpriteRevision);
        var lightHaloInstanceCount = _lightHalos.PrepareInstances(
            camera,
            worldLights,
            scene.Lighting,
            scene.Models.Count > 0
                ? new Vector3(
                    scene.Models[0].Position.X,
                    scene.Models[0].Position.Y,
                    scene.Models[0].GroundPlaneZ)
                : null,
            frame,
            renderWidth,
            renderHeight,
            worldSpriteRevision);
        var surfaceLightCount = _lightHalos.SurfaceLightCount;
        var worldLightBufferAddress = frame.LightHaloInstanceBuffer.GPUVirtualAddress;

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
                surfaceLightCount,
                scene.Lighting.NightBlend,
                worldLightBufferAddress,
                constants,
                rootSignature,
                terrainPipeline,
                liquidCoverPipeline,
                displayProfile.ScenePaperWhiteNits,
                renderWidth,
                renderHeight);

            if (_sprites.TryGetLiquidRange(image.Coord, out var liquidRange))
            {
                _sprites.RecordLiquid(
                    liquidRange,
                    scene.Lighting.WorldSurfaceAmbientColour,
                    displayProfile.ScenePaperWhiteNits,
                    surfaceLightCount,
                    scene.Lighting.NightBlend,
                    frame,
                    renderWidth,
                    renderHeight);
            }

            RecordTerrainLayer(
                texture.LiquidCoverSrvSlot,
                drawPosition.X,
                drawPosition.Y,
                drawWidth,
                drawHeight,
                scene.Lighting.WorldSurfaceAmbientColour,
                true,
                surfaceLightCount,
                scene.Lighting.NightBlend,
                worldLightBufferAddress,
                constants,
                rootSignature,
                terrainPipeline,
                liquidCoverPipeline,
                displayProfile.ScenePaperWhiteNits,
                renderWidth,
                renderHeight);

            if (scene.Debug.BlockedAreasVisible && image.HasBlockedAreaDebugData)
            {
                var debugPosition = screenTransform.ToScreen(
                    image.IsoX + image.BlockedAreaDebugOffsetX,
                    image.IsoY + image.BlockedAreaDebugOffsetY);
                RecordTerrainLayer(
                    texture.BlockedAreaDebugSrvSlot,
                    debugPosition.X,
                    debugPosition.Y,
                    screenTransform.Scale(image.BlockedAreaDebugWidth),
                    screenTransform.Scale(image.BlockedAreaDebugHeight),
                    Vector3.One,
                    true,
                    0,
                    0.0f,
                    worldLightBufferAddress,
                    constants,
                    rootSignature,
                    terrainPipeline,
                    liquidCoverPipeline,
                    displayProfile.ScenePaperWhiteNits,
                    renderWidth,
                    renderHeight);
            }
        }

        _commandList.OMSetRenderTargets(renderTarget, depthStencil);
        _sprites.RecordStaticShadows(
            spriteBatch,
            camera,
            scene.Lighting,
            frame,
            renderWidth,
            renderHeight);
        _commandList.ClearDepthStencilView(depthStencil, ClearFlags.Depth, 1.0f, 0, 0, []);
        _models.RecordShadows(camera, scene.Models, scene.Lighting, frame.Index);
        _sprites.RecordStatic(
            spriteBatch,
            scene.Lighting.WorldSurfaceAmbientColour,
            displayProfile.ScenePaperWhiteNits,
            displayProfile.UnlitSpriteNits,
            surfaceLightCount,
            scene.Lighting.NightBlend,
            frame,
            renderWidth,
            renderHeight);

        if (scene.Debug.StairsMapVisible)
        {
            RecordStairsDebug(
                sectorImages,
                screenTransform,
                constants,
                worldLightBufferAddress,
                rootSignature,
                terrainPipeline,
                liquidCoverPipeline,
                displayProfile.ScenePaperWhiteNits,
                renderTarget,
                renderWidth,
                renderHeight);
            _commandList.OMSetRenderTargets(renderTarget, depthStencil);
            _commandList.ClearDepthStencilView(depthStencil, ClearFlags.Depth, 1.0f, 0, 0, []);
        }

        if (scene.Debug.TerrainTopologyVisible)
        {
            RecordTerrainTopologyDebug(
                sectorImages,
                screenTransform,
                constants,
                worldLightBufferAddress,
                rootSignature,
                terrainPipeline,
                liquidCoverPipeline,
                displayProfile.ScenePaperWhiteNits,
                renderTarget,
                renderWidth,
                renderHeight);
            _commandList.OMSetRenderTargets(renderTarget, depthStencil);
            _commandList.ClearDepthStencilView(depthStencil, ClearFlags.Depth, 1.0f, 0, 0, []);
        }

        _models.Record(camera, scene.Models, scene.Lighting, displayProfile, frame.Index);

        // Light halos are screen-space overlays in Sacred and must remain above depth-tested art.
        _commandList.OMSetRenderTargets(renderTarget, null);
        _lightHalos.Record(
            lightHaloInstanceCount,
            scene.Lighting.NightBlend,
            displayProfile.UnlitSpriteNits,
            frame,
            renderWidth,
            renderHeight);

        _commandList.SetGraphicsRootSignature(rootSignature);
        _commandList.SetPipelineState(terrainPipeline);
        _commandList.SetGraphicsRootShaderResourceView(
            WorldQuadShaderLayout.WorldLightBufferRootParameter,
            worldLightBufferAddress);
        _commandList.OMSetRenderTargets(renderTarget, null);
        if (scene.Debug.OverlaysVisible)
            _debugOverlay.RecordDebugOverlay(renderWidth, renderHeight, displayProfile.UiPaperWhiteNits);
        if (scene.Minimap.IsVisible)
        {
            _minimap.Record(
                rootSignature,
                terrainPipeline,
                renderWidth,
                renderHeight,
                displayProfile.UiPaperWhiteNits);
        }
        _imgui.Record(frame, displayProfile.UiPaperWhiteNits);

        Dx12TextureUploader.Transition(
            _commandList,
            backBuffer,
            ResourceStates.RenderTarget,
            ResourceStates.Present);
    }

    private unsafe void RecordStairsDebug(
        IReadOnlyList<TerrainSectorComposition> sectorImages,
        WorldScreenTransform screenTransform,
        float* constants,
        ulong worldLightBufferAddress,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState terrainPipeline,
        ID3D12PipelineState liquidCoverPipeline,
        float paperWhiteNits,
        CpuDescriptorHandle renderTarget,
        int renderWidth,
        int renderHeight)
    {
        _commandList.OMSetRenderTargets(renderTarget, null);
        foreach (var image in sectorImages)
        {
            if (!_sectorTextures.TryGet(image.Coord, out var texture) || !image.HasStairsDebugData)
                continue;

            var debugPosition = screenTransform.ToScreen(
                image.IsoX + image.StairsDebugOffsetX,
                image.IsoY + image.StairsDebugOffsetY);
            RecordTerrainLayer(
                texture.StairsDebugSrvSlot,
                debugPosition.X,
                debugPosition.Y,
                screenTransform.Scale(image.StairsDebugWidth),
                screenTransform.Scale(image.StairsDebugHeight),
                Vector3.One,
                true,
                0,
                0.0f,
                worldLightBufferAddress,
                constants,
                rootSignature,
                terrainPipeline,
                liquidCoverPipeline,
                paperWhiteNits,
                renderWidth,
                renderHeight);
        }
    }

    private unsafe void RecordTerrainTopologyDebug(
        IReadOnlyList<TerrainSectorComposition> sectorImages,
        WorldScreenTransform screenTransform,
        float* constants,
        ulong worldLightBufferAddress,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState terrainPipeline,
        ID3D12PipelineState liquidCoverPipeline,
        float paperWhiteNits,
        CpuDescriptorHandle renderTarget,
        int renderWidth,
        int renderHeight)
    {
        _commandList.OMSetRenderTargets(renderTarget, null);
        foreach (var image in sectorImages)
        {
            if (!_sectorTextures.TryGet(image.Coord, out var texture))
                continue;

            var debugPosition = screenTransform.ToScreen(
                image.IsoX + image.TerrainTopologyDebugOffsetX,
                image.IsoY + image.TerrainTopologyDebugOffsetY);
            RecordTerrainLayer(
                texture.TerrainTopologyDebugSrvSlot,
                debugPosition.X,
                debugPosition.Y,
                screenTransform.Scale(image.TerrainTopologyDebugWidth),
                screenTransform.Scale(image.TerrainTopologyDebugHeight),
                Vector3.One,
                true,
                0,
                0.0f,
                worldLightBufferAddress,
                constants,
                rootSignature,
                terrainPipeline,
                liquidCoverPipeline,
                paperWhiteNits,
                renderWidth,
                renderHeight);
        }
    }

    private unsafe void RecordTerrainLayer(
        int srvSlot,
        float drawX,
        float drawY,
        float drawWidth,
        float drawHeight,
        Vector3 ambientColour,
        bool premultipliedAlpha,
        int worldLightCount,
        float nightBlend,
        ulong worldLightBufferAddress,
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
                paperWhiteNits,
                worldLightCount,
                nightBlend));
        _commandList.SetGraphicsRoot32BitConstants(
            WorldQuadShaderLayout.RootConstantsRootParameter,
            WorldQuadShaderLayout.RootConstantsCount,
            constants,
            0);
        _commandList.SetGraphicsRootDescriptorTable(
            WorldQuadShaderLayout.TextureRootParameter,
            _srvHeapStart + srvSlot * _srvDescriptorSize);
        _commandList.SetGraphicsRootShaderResourceView(
            WorldQuadShaderLayout.WorldLightBufferRootParameter,
            worldLightBufferAddress);
        _commandList.DrawInstanced(6, 1, 0, 0);
    }
}
