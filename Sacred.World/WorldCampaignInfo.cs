using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;

namespace Sacred.World;

/// <summary>Campaign metadata and authored map labels for presentation, without executable scripts.</summary>
public sealed record WorldCampaignInfo(
    SacredCampaignFiles Files,
    int FunctionCount,
    int PositionCount,
    int QuestCodeLength,
    int QuestPoolCodeLength,
    IReadOnlyList<SacredMapLabel> MapLabels);
