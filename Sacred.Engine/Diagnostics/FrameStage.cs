using System;

namespace Sacred.Engine.Diagnostics;

/// <summary>Timed regions of one frame, declared in display order (a pre-order walk of the stage tree).
/// Add a stage here and a matching row in <see cref="FrameStages"/>; a startup check rejects mismatches.</summary>
internal enum FrameStage
{
    // Waiting before the frame starts; not part of the active frame.
    LatencyWait,
    PacingWait,
    AcquireFrame,
    AcquireSlotWait,
    AcquireFenceWait,

    // Everything from the end of the waits to the end of Update, including present.
    Active,
    WindowMessages,
    RuntimeUpdate,
    ServerNetwork,
    SceneUpdate,
    UiInput,
    PlayerInput,
    WorldStreaming,
    Combat,
    Footprints,
    Doors,
    CharacterAnimation,
    ParticleUpdate,
    Weather,
    DebugUiBegin,
    Render,
    PrepareWorld,
    CullTerrain,
    CullLiquid,
    CullStatic,
    PreloadWorld,
    SectorTextures,
    BeginSubmission,
    UploadAndRecord,
    ModelPrepare,
    ModelTextures,
    SpriteTextures,
    LightHaloTextures,
    GpuParticlePrepare,
    MinimapPrepare,
    DebugUiBuild,
    RecordWorld,
    SpriteInstances,
    LightInstances,
    LightMaps,
    RecordTerrain,
    RecordFloor,
    RecordShadows,
    RecordObjects,
    RecordParticles,
    RecordLightHalos,
    RecordHighlight,
    RecordWeather,
    Upscale,
    RecordUi,
    SubmitPresent,
    Present,
    StreamingDispatch,

    /// <summary>Wall-clock time between frame starts, including every wait.</summary>
    FrameInterval
}

/// <summary>Display metadata for <see cref="FrameStage"/>: label, parent and nesting depth.</summary>
internal static class FrameStages
{
    private readonly record struct Row(FrameStage Stage, FrameStage? Parent, string Name);

    private static readonly Row[] Rows =
    [
        new(FrameStage.LatencyWait, null, "Latency limiter wait"),
        new(FrameStage.PacingWait, null, "CPU frame limiter wait"),
        new(FrameStage.AcquireFrame, null, "Acquire frame"),
        new(FrameStage.AcquireSlotWait, FrameStage.AcquireFrame, "DXGI slot wait"),
        new(FrameStage.AcquireFenceWait, FrameStage.AcquireFrame, "GPU fence wait"),

        new(FrameStage.Active, null, "Active frame"),
        new(FrameStage.WindowMessages, FrameStage.Active, "Window messages"),
        new(FrameStage.RuntimeUpdate, FrameStage.Active, "Runtime update"),
        new(FrameStage.ServerNetwork, FrameStage.RuntimeUpdate, "Server / network"),
        new(FrameStage.SceneUpdate, FrameStage.RuntimeUpdate, "Scene update"),
        new(FrameStage.UiInput, FrameStage.SceneUpdate, "UI input (menus, inventory, HUD)"),
        new(FrameStage.PlayerInput, FrameStage.SceneUpdate, "Player input & movement"),
        new(FrameStage.WorldStreaming, FrameStage.PlayerInput, "World streaming / visible sectors"),
        new(FrameStage.Combat, FrameStage.SceneUpdate, "Combat actions"),
        new(FrameStage.Footprints, FrameStage.SceneUpdate, "Footprints"),
        new(FrameStage.Doors, FrameStage.SceneUpdate, "Doors & world objects"),
        new(FrameStage.CharacterAnimation, FrameStage.SceneUpdate, "Character animation batch"),
        new(FrameStage.ParticleUpdate, FrameStage.SceneUpdate, "Particle simulation (CPU)"),
        new(FrameStage.Weather, FrameStage.SceneUpdate, "Weather"),
        new(FrameStage.DebugUiBegin, FrameStage.Active, "ImGui begin frame"),
        new(FrameStage.Render, FrameStage.Active, "Render"),
        new(FrameStage.PrepareWorld, FrameStage.Render, "Prepare world (culling)"),
        new(FrameStage.CullTerrain, FrameStage.PrepareWorld, "Cull sectors / ground tiles"),
        new(FrameStage.CullLiquid, FrameStage.PrepareWorld, "Cull liquid sprites"),
        new(FrameStage.CullStatic, FrameStage.PrepareWorld, "Cull static sprites"),
        new(FrameStage.PreloadWorld, FrameStage.PrepareWorld, "Preload candidates"),
        new(FrameStage.SectorTextures, FrameStage.PrepareWorld, "Sector texture residency"),
        new(FrameStage.BeginSubmission, FrameStage.Render, "Begin submission"),
        new(FrameStage.UploadAndRecord, FrameStage.Render, "Upload & record"),
        new(FrameStage.ModelPrepare, FrameStage.UploadAndRecord, "Model geometry / skin / pose prep"),
        new(FrameStage.ModelTextures, FrameStage.UploadAndRecord, "Model textures"),
        new(FrameStage.SpriteTextures, FrameStage.UploadAndRecord, "Sprite textures & cutouts"),
        new(FrameStage.LightHaloTextures, FrameStage.UploadAndRecord, "Light halo textures"),
        new(FrameStage.GpuParticlePrepare, FrameStage.UploadAndRecord, "GPU particle prep"),
        new(FrameStage.MinimapPrepare, FrameStage.UploadAndRecord, "Minimap prep"),
        new(FrameStage.DebugUiBuild, FrameStage.UploadAndRecord, "Debug UI / HUD build"),
        new(FrameStage.RecordWorld, FrameStage.UploadAndRecord, "Record world commands"),
        new(FrameStage.SpriteInstances, FrameStage.RecordWorld, "Sprite instances (visibility, sort)"),
        new(FrameStage.LightInstances, FrameStage.RecordWorld, "Light halo instances"),
        new(FrameStage.LightMaps, FrameStage.RecordWorld, "Surface light & occlusion maps"),
        new(FrameStage.RecordTerrain, FrameStage.RecordWorld, "Terrain & liquid"),
        new(FrameStage.RecordFloor, FrameStage.RecordWorld, "Floor sprites"),
        new(FrameStage.RecordShadows, FrameStage.RecordWorld, "Shadows"),
        new(FrameStage.RecordObjects, FrameStage.RecordWorld, "Objects & models"),
        new(FrameStage.RecordParticles, FrameStage.RecordWorld, "GPU particles"),
        new(FrameStage.RecordLightHalos, FrameStage.RecordWorld, "Light halos"),
        new(FrameStage.RecordHighlight, FrameStage.RecordWorld, "Highlighted object"),
        new(FrameStage.RecordWeather, FrameStage.RecordWorld, "Weather"),
        new(FrameStage.Upscale, FrameStage.Render, "Upscale pass"),
        new(FrameStage.RecordUi, FrameStage.Render, "Record UI (HUD, ImGui)"),
        new(FrameStage.SubmitPresent, FrameStage.Render, "Submit & present"),
        new(FrameStage.Present, FrameStage.SubmitPresent, "Present"),
        new(FrameStage.StreamingDispatch, FrameStage.SubmitPresent, "Streaming dispatch"),

        new(FrameStage.FrameInterval, null, "Frame interval (wall clock)")
    ];

    public static int Count { get; } = Rows.Length;
    private static readonly int[] ParentIndex = new int[Rows.Length];
    private static readonly int[] DepthByStage = new int[Rows.Length];
    private static readonly string[] Labels = new string[Rows.Length];

    static FrameStages()
    {
        if (Rows.Length != Enum.GetValues<FrameStage>().Length)
            throw new InvalidOperationException("FrameStages rows do not match the FrameStage enum.");
        for (var i = 0; i < Rows.Length; i++)
        {
            if ((int)Rows[i].Stage != i)
                throw new InvalidOperationException($"FrameStages row {i} is {Rows[i].Stage}; rows must follow enum order.");
            ParentIndex[i] = Rows[i].Parent is { } parent ? (int)parent : -1;
            if (ParentIndex[i] >= i)
                throw new InvalidOperationException($"{Rows[i].Stage} must be declared after its parent.");
            DepthByStage[i] = ParentIndex[i] < 0 ? 0 : DepthByStage[ParentIndex[i]] + 1;
            // Indentation is baked into the label once so drawing the table does not allocate.
            Labels[i] = new string(' ', DepthByStage[i] * 3) + Rows[i].Name;
        }
    }

    public static int ParentOf(int stage) => ParentIndex[stage];
    public static int DepthOf(int stage) => DepthByStage[stage];
    public static string Label(int stage) => Labels[stage];
}
