using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x483252..0x4832BA alternative CreateNPC placement. The
/// sector-flag predicate selecting this branch belongs to the caller. Native
/// registration runs before flag 2 is cleared, so it does not insert into sector
/// vectors. This path does not link the actor into the tile object chain.</summary>
public static class WorldCreatureScriptPlacement
{
    public static WorldCreatureInitialActor PlaceDormant(WorldCreatureInitialActor actor,
        GoldObjectPosition validatedPosition, WorldObjectActivationQueue activation)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(activation);
        if ((actor.Base.Flags & 2) == 0 || actor.Base.ObjectReference <= 16 ||
            validatedPosition.Sector > ushort.MaxValue || validatedPosition.Surface > byte.MaxValue)
            throw new NotSupportedException("Direct script placement requires an initial unplaced NPC and a validated native position.");
        var placed = actor with { Base = actor.Base with { Sector = (ushort)validatedPosition.Sector,
            X = validatedPosition.X, Y = validatedPosition.Y, Surface = (byte)validatedPosition.Surface, Flags = actor.Base.Flags & ~2u } };
        if (placed.Node.Active && !activation.DeactivateInitial(placed))
            throw new InvalidOperationException("The initial actor is not registered in its activation queue.");
        return placed;
    }
}
