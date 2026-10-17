using Sacred.Core.World;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost;

/// <summary>File-derived declaration pieces for inspection/preparation. Conditional declarations are not an executed startup world.</summary>
public sealed class WorldBaseTriggerPlan
{
    private WorldBaseTriggerPlan(GoldPlannedMovementTrigger[] pieces) => Pieces = Array.AsReadOnly(pieces);
    public IReadOnlyList<GoldPlannedMovementTrigger> Pieces { get; }
    public static WorldBaseTriggerPlan Create(WorldCampaignScripts scripts, SacredSectorIndex sectors)
    {
        ArgumentNullException.ThrowIfNull(scripts);
        ArgumentNullException.ThrowIfNull(sectors);
        var grid = sectors.CreateGrid();
        var pieces = scripts.BaseTriggerDeclarations.SelectMany(definition => GoldMovementTriggerPlanner.Create(definition, grid)).ToArray();
        Console.WriteLine($"Campaign trigger plan: {scripts.Files.Name}, {pieces.Length} pieces in {pieces.Select(piece => piece.Sector).Distinct().Count()} authored sectors; startup execution pending.");
        return new(pieces);
    }
}
