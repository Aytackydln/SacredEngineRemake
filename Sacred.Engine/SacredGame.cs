using System;
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
            initialSaveState.BorderlessFullscreen);
        _renderer = new Dx12Renderer(
            _window,
            gameDirectory,
            _latency,
            initialSaveState.HdrEnabled,
            initialSaveState.HdrBrightness);
        _framePacing = new FramePacingController(
            _renderer,
            _latency,
            _window.DisplayRefreshRateHz,
            initialSaveState.FramePacingMode,
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
            WaitForFrameStart(frameId, cancellationToken);
            if (!_window.ProcessMessages())
                break;

            await Update(frameId, cancellationToken);
            frameId++;
        }
    }

    private async ValueTask Update(ulong frameId, CancellationToken cancellationToken)
    {
        var deltaSeconds = _framePacing.Tick();
        _renderer.BeginDebugUiFrame(deltaSeconds);
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
        _framePacing.WaitForFrameStart(cancellationToken);
        _framePacing.BeginLatencyFrame(frameId);
    }
}
