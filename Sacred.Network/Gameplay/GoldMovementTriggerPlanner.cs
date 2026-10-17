using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x63C970 recursive sector routing. Does not execute the declaration's script or callbacks.</summary>
public static class GoldMovementTriggerPlanner
{
    public static IReadOnlyList<GoldPlannedMovementTrigger> Create(SacredBaseTriggerDefinition definition, SacredSectorGrid grid)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(grid);
        var result = new List<GoldPlannedMovementTrigger>();
        Route(Math.Min(definition.First.X, definition.Last.X), Math.Min(definition.First.Y, definition.Last.Y),
            Math.Max(definition.First.X, definition.Last.X), Math.Max(definition.First.Y, definition.Last.Y));
        return result.AsReadOnly();

        void Route(int firstX, int firstY, int lastX, int lastY)
        {
            var sector = grid.GetSector(firstX, firstY);
            // Native pointer lookup precedes recursive splitting: a hole stops this entire branch.
            if (!grid.HasSector(sector)) return;
            const int width = GoldMovementTriggerLayout.SectorWidth;
            if (firstX / width < lastX / width)
            {
                Route((firstX / width + 1) * width, firstY, lastX, lastY);
                lastX = firstX / width * width + width - 1;
            }
            if (firstY / width < lastY / width)
            {
                Route(firstX, (firstY / width + 1) * width, lastX, lastY);
                lastY = firstY / width * width + width - 1;
            }
            var piece = new SacredBaseTriggerDefinition(definition.NameBytes,
                new(firstX, firstY, definition.First.Z), new(lastX, lastY, definition.Last.Z));
            result.Add(new(sector, GoldMovementTriggerRecord.Create(piece), definition.SourceOffset));
        }
    }
}
