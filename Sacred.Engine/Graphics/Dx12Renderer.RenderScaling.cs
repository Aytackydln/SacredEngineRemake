using System.Numerics;
using Sacred.Engine.Scene.InGame;
using Sacred.World.Geometry;
using Vortice;
using Vortice.Direct3D12;
using Vortice.Mathematics;

namespace Sacred.Engine.Graphics;

public sealed partial class Dx12Renderer
{
    private void RecordUpscalePass(SacredCamera camera)
    {
        _fsr2History.Ensure(
            _graphics.OutputWidth,
            _graphics.OutputHeight,
            _graphics.RenderWidth,
            _graphics.RenderHeight,
            _graphics.BackBufferFormat);
        var scalingMode = RenderScalingModeResolver.Resolve(RenderScalingMode,
            _graphics.RenderWidth, _graphics.RenderHeight, _graphics.OutputWidth, _graphics.OutputHeight);
        var temporal = scalingMode == RenderScalingMode.Fsr2;
        var motionAware = temporal || scalingMode == RenderScalingMode.Fsr1MotionAdaptive;
        var reprojection = motionAware
            ? CalculateCameraReprojection(camera)
            : new CameraReprojection(Vector2.Zero, 1.0f);
        var historyWeight = temporal && _fsr2History.IsValid ? TemporalHistoryWeight : 0.0f;
        Dx12TextureUploader.Transition(
            _graphics.CommandList,
            _graphics.CurrentBackBuffer,
            ResourceStates.Present,
            ResourceStates.RenderTarget);
        _graphics.CommandList.RSSetViewports(new Viewport(0, 0, _graphics.OutputWidth, _graphics.OutputHeight, 0, 1));
        _graphics.CommandList.RSSetScissorRects(new RawRect(0, 0, _graphics.OutputWidth, _graphics.OutputHeight));
        _graphics.CommandList.OMSetRenderTargets(_graphics.CurrentRenderTarget, null);
        _graphics.CommandList.SetDescriptorHeaps(1, _graphics.ShaderVisibleDescriptorHeaps);
        _screenPass.RecordUpscale(
            _upscaleRootSignature,
            _upscalePipeline,
            _graphics.OutputWidth,
            _graphics.OutputHeight,
            _graphics.DisplayProfile.UiPaperWhiteNits,
            _graphics.SceneColorSrvGpuHandle,
            scalingMode,
            reprojection.CameraMotionPixels,
            historyWeight,
            reprojection.HistoryUvScale);
        if (temporal)
            _fsr2History.Capture(_graphics.CurrentBackBuffer);
        else
        {
            _fsr2History.Reset();
            Dx12TextureUploader.Transition(
                _graphics.CommandList,
                _graphics.CurrentBackBuffer,
                ResourceStates.RenderTarget,
                ResourceStates.Present);
        }
    }

    private CameraReprojection CalculateCameraReprojection(SacredCamera camera)
    {
        var currentViewportZoom = camera.GetViewportZoom(_graphics.OutputHeight);
        if (_previousCameraCenter is not { } previousCenter ||
            _previousCameraViewportZoom is not { } previousViewportZoom ||
            !float.IsFinite(currentViewportZoom) || currentViewportZoom <= 0.0f ||
            !float.IsFinite(previousViewportZoom) || previousViewportZoom <= 0.0f)
        {
            return new CameraReprojection(Vector2.Zero, 1.0f);
        }

        var worldDelta = camera.WorldCenter - previousCenter;
        // World-center displacement becomes the current pixel's offset into the
        // previous output after isometric projection and output-space scaling.
        var cameraMotionPixels = IsometricProjection.WorldToIso(worldDelta) * previousViewportZoom;
        return float.IsFinite(cameraMotionPixels.X) && float.IsFinite(cameraMotionPixels.Y)
            ? new CameraReprojection(cameraMotionPixels, previousViewportZoom / currentViewportZoom)
            : new CameraReprojection(Vector2.Zero, 1.0f);
    }

    private void ResetFsr2History()
    {
        _fsr2History.Reset();
        _previousCameraCenter = null;
        _previousCameraViewportZoom = null;
    }

    private readonly record struct CameraReprojection(Vector2 CameraMotionPixels, float HistoryUvScale);

}
