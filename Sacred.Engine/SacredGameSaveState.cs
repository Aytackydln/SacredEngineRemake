using System.Numerics;
using Sacred.Engine.Graphics;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny.Abstractions;
using Sacred.Particles;
using Sacred.World;

namespace Sacred.Engine;

/// <summary>Remake runtime values that can be restored between launches.</summary>
public sealed record SacredGameSaveState
{
    /// <summary>Allows console-driven validation without explicitly focusing the window.</summary>
    public bool RequestWindowFocus { get; init; } = true;
    /// <summary>Whether the game uses a borderless window that fills the primary display.</summary>
    public bool BorderlessFullscreen { get; init; }
    /// <summary>Outer dimensions restored when leaving borderless fullscreen.</summary>
    public int WindowedWidth { get; init; } = 1600;
    public int WindowedHeight { get; init; } = 900;
    public int WindowedX { get; init; } = 100;
    public int WindowedY { get; init; } = 100;
    public bool WindowedMaximized { get; init; }
    public bool HdrEnabled { get; init; }
    public HdrBrightnessSettings HdrBrightness { get; init; } = HdrBrightnessSettings.Default;
    public FramePacingMode FramePacingMode { get; init; } = FramePacingMode.VariableRefreshRate;
    public int ManualFrameRate { get; init; } = 120;
    public LowLatencyMode LowLatencyMode { get; init; } = LowLatencyMode.On;
    public int RenderResolutionPercentage { get; init; } = 100;
    public bool AutoRenderResolution { get; init; } = true;
    public int AutoRenderResolutionMinimumPercentage { get; init; } = TileResolutionScaling.DefaultMinimumPercentage;
    public int AutoRenderResolutionMaximumPercentage { get; init; } = TileResolutionScaling.DefaultMaximumPercentage;
    public bool AutoRenderResolutionStepSnapping { get; init; } = true;
    public int AutoRenderResolutionStepPercentage { get; init; } = TileResolutionScaling.DefaultStepPercentage;
    public RenderScalingMode RenderScalingMode { get; init; } = RenderScalingMode.Bilinear;
    public GrnBackendKind GrannyBackend { get; init; } = GrnBackendKind.ManagedParser;
    public WorldLightingMode WorldLightingMode { get; init; } = WorldLightingMode.TimedDayNightCycle;
    public SectorLoadMode SectorLoadMode { get; init; } = SectorLoadMode.Four;
    public bool WaitForSectorGpuUploads { get; init; }
    public bool StairsTilesVisible { get; init; }
    public bool BlockedTilesVisible { get; init; }
    public float PlayerMovementSpeedMultiplier { get; init; } = 1.0f;
    public SacredParticleQuality ParticleQuality { get; init; } = SacredParticleQuality.High;
    public ParticleSimulationMode ParticleSimulation { get; init; } = ParticleSimulationMode.Auto;
    public SkinningMode SkinningMode { get; init; } = SkinningMode.Auto;
    public string? CharacterName { get; init; }
    public string? CampaignScriptsDirectory { get; init; }
    public Vector2? LastLocation { get; init; }
}
