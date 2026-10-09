using System.Collections.Generic;
using Sacred.Core.GameBin.Scripts;
using Sacred.Engine.Graphics.Skinning;
using Sacred.Engine.Latency;
using Sacred.Engine.Scene.InGame;
using Sacred.Particles;
using Sacred.World;

namespace Sacred.Engine.Graphics.ImGui;

/// <summary>Bridges ImGui requests to the normal engine update path.</summary>
internal sealed class DebugUiControlState
{
    public IReadOnlyList<SacredCampaignFiles> Campaigns { get; set; } = [];
    public string? CampaignDirectoryPath { get; set; }
    public string CampaignDisplayName { get; set; } = SacredCampaignFiles.DefaultDirectoryName;
    public bool CampaignChangeAvailable { get; set; }
    public string? RequestedCampaign { get; set; }
    public bool RequestedCampaignListRefresh { get; set; }
    public bool HdrEnabled { get; set; }
    public FramePacingMode FramePacingMode { get; set; }
    public int ManualFrameRate { get; set; } = 60;
    public LowLatencyMode LowLatencyMode { get; set; }
    public WorldLightingMode WorldLightingMode { get; set; }
    public SectorLoadMode SectorLoadMode { get; set; } = SectorLoadMode.Four;
    public bool WaitForSectorGpuUploads { get; set; }
    public SectorLoadMode? RequestedSectorLoadMode { get; set; }
    public bool? RequestedWaitForSectorGpuUploads { get; set; }
    public bool BorderlessFullscreen { get; set; }
    public SacredParticleQuality ParticleQuality { get; set; }
    public ParticleSimulationMode ParticleSimulation { get; set; } = ParticleSimulationMode.Auto;
    public SkinningMode SkinningMode { get; set; }
    public SkinningMode? RequestedSkinningMode { get; set; }
    public CollisionCheatMode CollisionMode { get; set; }
    public float PlayerMovementSpeedMultiplier { get; set; } = 1.0f;
    public int RenderResolutionPercentage { get; set; } = 100;
    public bool AutoRenderResolution { get; set; }
    public int AutoRenderResolutionMinimumPercentage { get; set; } = TileResolutionScaling.MinimumPercentage;
    public int AutoRenderResolutionMaximumPercentage { get; set; } = TileResolutionScaling.MaximumPercentage;
    public bool AutoRenderResolutionStepSnapping { get; set; }
    public int AutoRenderResolutionStepPercentage { get; set; } = TileResolutionScaling.DefaultStepPercentage;
    public RenderScalingMode RenderScalingMode { get; set; } = RenderScalingMode.Bilinear;
    public PlayerDebugPanelState? Player { get; set; }
    public bool PlayerPanelVisible { get; set; }
    public PlayerParticlePanelState? PlayerParticles { get; set; }
    public bool? RequestedParticlePanelOpen { get; set; }
    public bool? RequestedParticlePreviewMode { get; set; }
    public Queue<PlayerParticleRequest> ParticleRequests { get; } = new();

    public bool? RequestedHdrEnabled { get; set; }
    public FramePacingMode? RequestedFramePacingMode { get; set; }
    public int? RequestedManualFrameRate { get; set; }
    public LowLatencyMode? RequestedLowLatencyMode { get; set; }
    public WorldLightingMode? RequestedWorldLightingMode { get; set; }
    public bool? RequestedBorderlessFullscreen { get; set; }
    public SacredParticleQuality? RequestedParticleQuality { get; set; }
    public ParticleSimulationMode? RequestedParticleSimulation { get; set; }
    public CollisionCheatMode? RequestedCollisionMode { get; set; }
    public float? RequestedPlayerMovementSpeedMultiplier { get; set; }
    public int? RequestedRenderResolutionPercentage { get; set; }
    public bool? RequestedAutoRenderResolution { get; set; }
    public (int Minimum, int Maximum)? RequestedAutoRenderResolutionRange { get; set; }
    public (bool Enabled, int StepPercentage)? RequestedAutoRenderResolutionStepSnapping { get; set; }
    public RenderScalingMode? RequestedRenderScalingMode { get; set; }
    public int? RequestedPlayerEquipmentRemoval { get; set; }
    public bool RequestedPlayerRemoveAllEquipment { get; set; }
    public int? RequestedPlayerItemSet { get; set; }
    public uint? RequestedPlayerCharacter { get; set; }
    public bool ScreenshotRequested { get; set; }
}
