using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.World;

namespace Sacred.GameplayHost;

/// <summary>Projects literal campaign metadata into the data consumed by world presentation.
/// Conditional labels remain excluded until their startup effects are executed.</summary>
internal static class WorldCampaignPresentation
{
    public static WorldCampaignInfo Create(WorldCampaignScripts scripts)
    {
        var labels = new List<SacredMapLabel>();
        var depth = 0;
        foreach (var command in SacredCompiledScriptReader.Read(scripts.StartupCode))
        {
            if (command.Opcode == SacredScriptPortalOpcodes.If) { depth++; continue; }
            if (command.Opcode == SacredScriptPortalOpcodes.EndBlock) { depth = Math.Max(0, depth - 1); continue; }
            if (depth == 0 && SacredMapAnnotationReader.TryReadLabel(command, out var label)) labels.Add(label);
        }
        return new(scripts.Files, scripts.Functions.Count, scripts.Positions.Count,
            scripts.QuestCode.Length, scripts.QuestPoolCode.Length, labels.AsReadOnly());
    }
}
