using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Sacred.Core.World.Sector;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Scene;
using Sacred.Engine.Scene.InGame;
using Sacred.Shaders;
using Sacred.World.Particles;
using Sacred.World.Rendering;

namespace Sacred.Engine.Graphics;

public sealed partial class Dx12Renderer
{
    private ulong _offlineParticleRevision;
    private Dx12AreaDownsamplePass? _areaDownsample;
    internal void SetOffscreenAnimationTime(float seconds) => GetWorldPass().SetAnimationTime(seconds);
    internal Dx12Renderer(string gameDirectory, int width, int height, LowLatencySystem latency)
    {
        _gameDirectory = gameDirectory;
        _input = new InputState();
        _shaderReloadHandler = RequestShaderReload;
        _releaseRetiredResources = ReleaseRetiredResources;
        try
        {
            _graphics = new Dx12DeviceContext(width, height, latency, Dx12DescriptorLayout.TotalCount);
            _textureUploader = new Dx12TextureUploader(_graphics.Device);
            FrameTiming.Animation.ReadGpuTimings = _graphics.GpuAnimationTimings.CaptureAndReset;
            _screenPass = new Dx12ScreenPass(_graphics.CommandList, _textureUploader,
                _graphics.SrvCpuHandle(Dx12DescriptorLayout.Screen), _graphics.SrvGpuHandle(Dx12DescriptorLayout.Screen));
            _fsr2History = new Dx12Fsr2History(_graphics.Device, _graphics.CommandList,
                _graphics.SrvCpuHandle(Dx12DescriptorLayout.Fsr2History));
            CreatePipeline();
            _screenshotWriter = new Dx12ScreenshotWriterQueue(gameDirectory);
        }
        catch
        {
            DisposePipelineResources();
            _fsr2History?.Dispose();
            _screenPass?.Dispose();
            _graphics?.Dispose();
            throw;
        }
        Dx12ShaderCatalog.Reloaded += _shaderReloadHandler;
    }

    internal async Task<RgbaImage> RenderImageAsync(int width, int height, int outputWidth, int outputHeight,
        SacredCamera camera, VisibleWorld world, SceneState scene, WorldParticleSystem particles)
    {
        _graphics.SetOffscreenSize(outputWidth, outputHeight);
        _graphics.SetRenderResolution(width, height);
        GetWorldPass().EnableBulkUploads();
        var timer = Stopwatch.StartNew();
        var frameId = 0UL;
        var particleRevision = ++_offlineParticleRevision;
        string? previousPending = null;
        while (true)
        {
            PrepareFrame(default);
            var pass = GetWorldPass();
            var prepared = pass.Prepare(camera, world, scene, particles.Particles, particleRevision);
            _graphics.BeginRenderSubmission(_terrainPipeline);
            pass.UploadPreload(new WorldPreloadRequest(camera, world, scene), prepared);
            var ready = pass.LastPreparationStatus.IsReady;
            var pending = pass.LastPreparationStatus.PendingItem;
            if (pending != previousPending)
            {
                Console.WriteLine($"GPU preparation: {timer.Elapsed.TotalMilliseconds:F0} ms, frame {frameId}, {pending}.");
                previousPending = pending;
            }
            if (ready)
            {
                pass.UploadAndRecord(camera, world, scene, prepared, "Offscreen export", _rootSignature,
                    _terrainPipeline, _terrainLiquidCoverPipeline, _shadowOverlayPipeline);
                if (_graphics.UsesRenderScaling)
                {
                    _areaDownsample ??= new Dx12AreaDownsamplePass(_graphics.Device, _graphics.BackBufferFormat);
                    _areaDownsample.Record(_graphics);
                }
            }
            using var capture = _graphics.SubmitAndPresent(false, frameId++, ready);
            pass.OnForegroundFrameSubmitted();
            if (capture is not null)
            {
                var image = capture.WaitAndRead();
                // The engine's SDR capture is BGRA; offline image writers use RGBA.
                for (var i = 0; i < image.Pixels.Length; i += 4)
                    (image.Pixels[i], image.Pixels[i + 2]) = (image.Pixels[i + 2], image.Pixels[i]);
                return new RgbaImage(image.Width, image.Height, image.Pixels);
            }
            if (timer.Elapsed > TimeSpan.FromMinutes(2))
                throw new TimeoutException($"GPU world preparation timed out: {pass.LastPreparationStatus}");
            await Task.Delay(5).ConfigureAwait(false);
        }
    }

    internal void ClearOffscreenWorld()
    {
        _worldPass?.StopBackgroundWork();
        _graphics.WaitForGpu(_releaseRetiredResources);
        _worldPass?.DisposePipelines();
        _worldPass?.Dispose();
        _worldPass = null;
    }
}
