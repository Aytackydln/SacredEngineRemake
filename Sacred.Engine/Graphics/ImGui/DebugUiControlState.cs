using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;
using Sacred.Particles;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Bridges ImGui requests to the normal engine update path.</summary>
internal sealed class DebugUiControlState
{
    public bool HdrEnabled { get; set; }
    public FramePacingMode FramePacingMode { get; set; }
    public LowLatencyMode LowLatencyMode { get; set; }
    public WorldLightingMode WorldLightingMode { get; set; }
    public bool BorderlessFullscreen { get; set; }
    public SacredParticleQuality ParticleQuality { get; set; }
    public CollisionCheatMode CollisionMode { get; set; }
    public float PlayerMovementSpeedMultiplier { get; set; } = 1.0f;
    public int RenderResolutionPercentage { get; set; } = 100;
    public RenderScalingMode RenderScalingMode { get; set; } = RenderScalingMode.Bilinear;
    public PlayerDebugPanelState? Player { get; set; }
    public bool PlayerPanelVisible { get; set; }

    public bool? RequestedHdrEnabled { get; set; }
    public FramePacingMode? RequestedFramePacingMode { get; set; }
    public LowLatencyMode? RequestedLowLatencyMode { get; set; }
    public WorldLightingMode? RequestedWorldLightingMode { get; set; }
    public bool? RequestedBorderlessFullscreen { get; set; }
    public SacredParticleQuality? RequestedParticleQuality { get; set; }
    public CollisionCheatMode? RequestedCollisionMode { get; set; }
    public float? RequestedPlayerMovementSpeedMultiplier { get; set; }
    public int? RequestedRenderResolutionPercentage { get; set; }
    public RenderScalingMode? RequestedRenderScalingMode { get; set; }
    public int? RequestedPlayerEquipmentRemoval { get; set; }
    public int? RequestedPlayerItemSet { get; set; }
    public uint? RequestedPlayerCharacter { get; set; }
    public bool ScreenshotRequested { get; set; }
}
