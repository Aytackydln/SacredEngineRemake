using Sacred.Core.World.Sector;
using Sacred.Engine.Graphics.ImGui;
using Sacred.Engine.Graphics.Minimap;
using Sacred.Engine.Rendering;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Vortice;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

/// <summary>Records world UI and prepares its debug panels independently of world streaming.</summary>
internal sealed class Dx12WorldUiPass(
    Dx12DeviceContext graphics,
    Dx12WorldCommandRecorder commandRecorder,
    Dx12MinimapPass minimap,
    Dx12DebugOverlay debugOverlay,
    Dx12ImGuiRenderer imgui,
    ImGuiDebugPanel debugPanel)
{
    public void PrepareWorldMap(WorldMapOverlay overlay)
    {
        if (overlay.MinimapVisible)
        {
            minimap.Prepare(
                overlay.TargetWorldPosition,
                overlay.DifficultyDisplayName,
                overlay.RegionDisplayName,
                graphics.RenderWidth,
                graphics.RenderHeight,
                graphics.CurrentFrame);
        }
        else if (overlay.TargetMarkerVisible)
        {
            minimap.PrepareTargetMarker(graphics.CurrentFrame);
        }
    }

    public void RecordUi(
        SceneState scene,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState terrainPipeline)
    {
        Dx12TextureUploader.Transition(
            graphics.CommandList,
            graphics.CurrentBackBuffer,
            ResourceStates.Present,
            ResourceStates.RenderTarget);
        graphics.CommandList.RSSetViewports(new Viewport(
            0, 0, graphics.OutputWidth, graphics.OutputHeight, 0, 1));
        graphics.CommandList.RSSetScissorRects(new RawRect(
            0, 0, graphics.OutputWidth, graphics.OutputHeight));
        graphics.CommandList.OMSetRenderTargets(graphics.CurrentRenderTarget, null);
        graphics.CommandList.SetDescriptorHeaps(1, graphics.ShaderVisibleDescriptorHeaps);
        commandRecorder.RecordUi(
            scene,
            graphics.CurrentFrame,
            rootSignature,
            terrainPipeline,
            graphics.DisplayProfile,
            graphics.OutputWidth,
            graphics.OutputHeight);
        Dx12TextureUploader.Transition(
            graphics.CommandList,
            graphics.CurrentBackBuffer,
            ResourceStates.RenderTarget,
            ResourceStates.Present);
    }

    public void RecordWorldMap(
        WorldMapOverlay overlay,
        ID3D12RootSignature rootSignature,
        ID3D12PipelineState pipeline)
    {
        if (overlay.MinimapVisible)
        {
            minimap.Record(
                rootSignature,
                pipeline,
                graphics.OutputWidth,
                graphics.OutputHeight,
                graphics.DisplayProfile.UiWhiteScale);
        }
        if (overlay.TargetMarkerVisible)
        {
            minimap.RecordTargetMarker(
                rootSignature,
                pipeline,
                overlay.TargetScreenPosition,
                graphics.OutputWidth,
                graphics.OutputHeight,
                graphics.DisplayProfile.UiWhiteScale);
        }
    }

    public void UpdateDebug(SacredCamera camera, VisibleWorld world, SceneState scene,
        Dx12PreparedWorldFrame prepared, Dx12DebugOverlayStats debugStats)
    {
        debugOverlay.Update(
            camera,
            world,
            debugStats,
            graphics.IsHdrEnabled,
            graphics.RenderWidth,
            graphics.RenderHeight,
            graphics.CurrentFrame.TransientResources,
            scene.Indoor.ActiveGroup?.SurfaceLevel ?? 0);
        if (imgui.IsFrameBegun)
        {
            debugPanel.Build(
                camera,
                world,
                scene,
                debugStats,
                prepared.StaticSprites,
                prepared.WorldLights,
                debugOverlay.FramesPerSecond,
                graphics.OutputWidth,
                graphics.OutputHeight);
        }
    }
}
