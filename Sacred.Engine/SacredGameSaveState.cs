using System.Numerics;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;
using Sacred.Granny;
using Sacred.Granny.Abstractions;

namespace Sacred.Engine;

/// <summary>Remake runtime values that can be restored between launches.</summary>
public sealed record SacredGameSaveState
{
    /// <summary>Whether the game uses a borderless window that fills the primary display.</summary>
    public bool BorderlessFullscreen { get; init; } = true;
    /// <summary>Outer dimensions restored when leaving borderless fullscreen.</summary>
    public int WindowedWidth { get; init; } = 1600;
    public int WindowedHeight { get; init; } = 900;
    public bool HdrEnabled { get; init; }
    public FramePacingMode FramePacingMode { get; init; } = FramePacingMode.VariableRefreshRate;
    public LowLatencyMode LowLatencyMode { get; init; } = LowLatencyMode.On;
    public GrnBackendKind GrannyBackend { get; init; } = GrnBackendKind.ManagedParser;
    public WorldLightingMode WorldLightingMode { get; init; } = WorldLightingMode.TimedDayNightCycle;
    public bool StairsTilesVisible { get; init; }
    public bool BlockedTilesVisible { get; init; }
    public string? CharacterName { get; init; }
    public Vector2? LastLocation { get; init; }
}
