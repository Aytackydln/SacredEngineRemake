using System.Numerics;
using Sacred.Engine.Graphics;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny.Abstractions;
using Sacred.Particles;

namespace Sacred.Engine;

/// <summary>Remake runtime values that can be restored between launches.</summary>
public sealed record SacredGameSaveState
{
    /// <summary>Whether the game uses a borderless window that fills the primary display.</summary>
    public bool BorderlessFullscreen { get; init; } = true;
    /// <summary>Outer dimensions restored when leaving borderless fullscreen.</summary>
    public int WindowedWidth { get; init; } = 1600;
    public int WindowedHeight { get; init; } = 900;
    public int WindowedX { get; init; } = 100;
    public int WindowedY { get; init; } = 100;
    public bool WindowedMaximized { get; init; }
    public bool HdrEnabled { get; init; }
    public HdrBrightnessSettings HdrBrightness { get; init; } = HdrBrightnessSettings.Default;
    public FramePacingMode FramePacingMode { get; init; } = FramePacingMode.VariableRefreshRate;
    public LowLatencyMode LowLatencyMode { get; init; } = LowLatencyMode.On;
    public int RenderResolutionPercentage { get; init; } = 100;
    public RenderScalingMode RenderScalingMode { get; init; } = RenderScalingMode.Bilinear;
    public GrnBackendKind GrannyBackend { get; init; } = GrnBackendKind.ManagedParser;
    public WorldLightingMode WorldLightingMode { get; init; } = WorldLightingMode.TimedDayNightCycle;
    public bool StairsTilesVisible { get; init; }
    public bool BlockedTilesVisible { get; init; }
    public float PlayerMovementSpeedMultiplier { get; init; } = 1.0f;
    public SacredParticleQuality ParticleQuality { get; init; } = SacredParticleQuality.High;
    public string? CharacterName { get; init; }
    public Vector2? LastLocation { get; init; }
}
