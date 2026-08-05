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
    Action updateWindowTitle)
{
    public void Update()
    {
        if (input.ConsumePressed(VirtualKey.F4))
            renderer.ToggleHdr();

        if (input.ConsumePressed(VirtualKey.F5))
        {
            cycleFramePacing();
            updateWindowTitle();
        }

        if (input.ConsumePressed(VirtualKey.F6))
        {
            latency.CycleMode();
            updateWindowTitle();
        }
    }
}
