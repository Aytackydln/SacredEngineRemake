using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Sacred.Core;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;
using Sacred.Engine.Platform;
using Sacred.Engine.Scene;

namespace Sacred.Engine;

/// <summary>Owns the frame loop and coordinates latency, simulation, and rendering.</summary>
public sealed class SacredGame : IDisposable
{
    private readonly Win32Window _window;
    private readonly LowLatencySystem _latency;
    private readonly Dx12Renderer _renderer;
    private readonly SceneManager _scenes = new();
    private readonly FramePacingController _framePacing;
    private readonly SacredGameRuntime _runtime;
    private double _lastCompletedFrameTimeMilliseconds;
    private bool _disposed;

    public SacredGame(SacredGameDirectories gameDirectories, SacredGameSaveState? saveState = null)
    {
        ArgumentNullException.ThrowIfNull(gameDirectories);
        var initialSaveState = SacredGameRuntime.NormalizeSaveState(saveState ?? new SacredGameSaveState());
        var gameDirectory = SacredGameRuntime.ResolveGameDirectory(gameDirectories);

        _latency = LowLatencySystem.CreateDefault();
        _window = new Win32Window(
            "Sacred Remake",
            initialSaveState.WindowedWidth,
            initialSaveState.WindowedHeight,
            initialSaveState.BorderlessFullscreen,
            initialSaveState.WindowedX,
            initialSaveState.WindowedY,
            initialSaveState.WindowedMaximized);
        _renderer = new Dx12Renderer(
            _window,
            gameDirectory,
            _latency,
            initialSaveState.HdrEnabled,
            initialSaveState.HdrBrightness,
            initialSaveState.RenderResolutionPercentage,
            initialSaveState.RenderScalingMode,
            initialSaveState.AutoRenderResolution,
            initialSaveState.AutoRenderResolutionMinimumPercentage,
            initialSaveState.AutoRenderResolutionMaximumPercentage,
            initialSaveState.AutoRenderResolutionStepSnapping,
            initialSaveState.AutoRenderResolutionStepPercentage);
        _framePacing = new FramePacingController(
            _renderer,
            _latency,
            _window.DisplayRefreshRateHz,
            initialSaveState.FramePacingMode,
            initialSaveState.ManualFrameRate,
            initialSaveState.LowLatencyMode);
        _runtime = new SacredGameRuntime(
            gameDirectories,
            initialSaveState,
            gameDirectory,
            _window,
            _latency,
            _renderer,
            _framePacing,
            _scenes);
    }

    public Task Run(CancellationToken cancellationToken = default) =>
        Win32AsyncPump.RunAsync(() => RunCoreAsync(cancellationToken), _window.ProcessMessages);

    public SacredGameSaveState CaptureSaveState() => _runtime.CaptureSaveState();

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        // GPU caches can reference scene-owned assets, so retire them before disposing scenes.
        _renderer.Dispose();
        _framePacing.Dispose();
        _runtime.Dispose();
        _latency.Dispose();
        _window.Dispose();
    }

    private async Task RunCoreAsync(CancellationToken cancellationToken)
    {
        var frameId = 0UL;
        while (!cancellationToken.IsCancellationRequested)
        {
            var waitStart = _renderer.FrameTiming.Enabled ? Stopwatch.GetTimestamp() : 0;
            WaitForFrameStart(frameId, cancellationToken);
            var frameStartTimestamp = Stopwatch.GetTimestamp();
            if (!_window.ProcessMessages())
                break;

            await Update(frameId, cancellationToken);
            _lastCompletedFrameTimeMilliseconds =
                Stopwatch.GetElapsedTime(frameStartTimestamp).TotalMilliseconds;
            if (waitStart != 0 && _renderer.FrameTiming.Enabled)
                _renderer.FrameTiming.Record(
                    (frameStartTimestamp - waitStart) * 1000.0 / Stopwatch.Frequency,
                    _lastCompletedFrameTimeMilliseconds, _renderer.LastPresentMilliseconds,
                    $"{_scenes.ActiveSceneId}, HDR {_renderer.IsHdrEnabled}, {_renderer.RenderWidth}x{_renderer.RenderHeight}, {_framePacing.Status}, {_latency.ActiveBackendName} {_latency.Mode}");
            frameId++;
        }
    }

    private async ValueTask Update(ulong frameId, CancellationToken cancellationToken)
    {
        var deltaSeconds = _framePacing.Tick();
        _renderer.BeginDebugUiFrame(deltaSeconds, _lastCompletedFrameTimeMilliseconds);
        _latency.Mark(LatencyMarker.SimulationStart, frameId);
        if (_window.Input.HasPendingLeftClick)
            _latency.Mark(LatencyMarker.LeftMouseButtonClick, frameId);

        _runtime.Update(deltaSeconds);
        _latency.Mark(LatencyMarker.SimulationEnd, frameId);

        await _scenes.ActiveScene.RenderAsync(new SceneRenderContext(
            _renderer,
            _framePacing.VerticalSyncEnabled,
            _framePacing.Status,
            frameId,
            cancellationToken));
    }

    private void WaitForFrameStart(ulong frameId, CancellationToken cancellationToken)
    {
        // Complete driver and CPU pacing before consuming the DXGI presentation slot.
        _framePacing.BeginLatencyFrame(frameId);
        _framePacing.WaitForFrameStart(cancellationToken);
    }
}
