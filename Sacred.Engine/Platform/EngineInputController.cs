using System;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;

namespace Sacred.Engine.Platform;

/// <summary>Handles engine-wide controls before input is routed to the active scene.</summary>
internal sealed class EngineInputController(
    InputState input,
    Dx12Renderer renderer,
    LowLatencySystem latency,
    Action cycleFramePacing,
    Func<bool> toggleBorderlessFullscreen,
    Action updateWindowTitle)
{
    public void Update()
    {
        if (input.ConsumePressed(VirtualKey.F4))
        {
            var enabled = renderer.ToggleHdr();
            EngineLog.WriteLine($"Debug input: HDR {(enabled ? "enabled" : "disabled")}");
        }

        if (input.ConsumePressed(VirtualKey.F5))
        {
            cycleFramePacing();
            updateWindowTitle();
            EngineLog.WriteLine("Debug input: frame pacing cycled");
        }

        if (input.ConsumePressed(VirtualKey.F6))
        {
            var mode = latency.CycleMode();
            updateWindowTitle();
            EngineLog.WriteLine($"Debug input: low latency {mode}");
        }

        if (input.ConsumePressed(VirtualKey.F10))
        {
            var fullscreen = toggleBorderlessFullscreen();
            updateWindowTitle();
            EngineLog.WriteLine($"Debug input: {(fullscreen ? "borderless fullscreen" : "windowed mode")}");
        }
    }
}
