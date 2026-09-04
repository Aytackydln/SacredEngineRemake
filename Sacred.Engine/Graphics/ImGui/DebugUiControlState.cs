using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Bridges ImGui requests to the normal engine update path.</summary>
internal sealed class DebugUiControlState
{
    public bool HdrEnabled { get; set; }
    public FramePacingMode FramePacingMode { get; set; }
    public LowLatencyMode LowLatencyMode { get; set; }
    public WorldLightingMode WorldLightingMode { get; set; }
    public bool BorderlessFullscreen { get; set; }

    public bool? RequestedHdrEnabled { get; set; }
    public FramePacingMode? RequestedFramePacingMode { get; set; }
    public LowLatencyMode? RequestedLowLatencyMode { get; set; }
    public WorldLightingMode? RequestedWorldLightingMode { get; set; }
    public bool? RequestedBorderlessFullscreen { get; set; }
    public bool ScreenshotRequested { get; set; }
}
