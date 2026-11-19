using Sacred.Core.World;
using Sacred.Core.World.Sector;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Supported 5FE930(sector,1): traverse the actual member vector and
/// initial registry, validate the cached sector/position and call 640640 on
/// active and supported unloaded dormant nodes. Reference repair/removal,
/// position-sector reassignment and other activation callbacks retain guards.</summary>
public static class WorldCreatureSectorActivation
{
    public static IReadOnlyList<WorldCreatureInitialActor> Prepare(ushort sector, SectorCoord coordinate,
        WorldCreatureInitialFactory factory, WorldCreatureSectorMembership membership,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem>? items, IReadOnlyList<WorldScriptCreatedEffect>? effects)
    {
        var result = new List<WorldCreatureInitialActor>();
        foreach (var reference in membership.Get(sector))
        {
            var actor = factory.Actors.GetValueOrDefault(reference);
            var effect = actor is null ? effects?.SingleOrDefault(value => value.BaseState.ObjectReference == reference) : null;
            var state = actor?.Base ?? effect?.BaseState;
            if (reference == 0 || state is not { } identity || identity.ObjectReference != reference)
                throw new NotSupportedException("Sector activation requires every actual member in the same current factory; native reference repair/removal remains separate.");
            var x = SacredWorldCoordinates.WorldToTile(identity.X);
            var y = SacredWorldCoordinates.WorldToTile(identity.Y);
            if (identity.Sector != sector || x < coordinate.X * 64 || x >= (coordinate.X + 1) * 64 ||
                y < coordinate.Y * 64 || y >= (coordinate.Y + 1) * 64)
                throw new NotSupportedException("Sector activation requires the native cached-sector position match; reassignment remains separate.");
            if (actor is not null)
            {
                WorldCreatureSectorReactivation.Require(actor, factory.Activation, items);
                result.Add(actor);
            }
            else if (effect is null || !effect.Node.Active || !factory.Activation.Nodes.Contains(effect.Node))
                throw new NotSupportedException("Sector activation requires its actual already active Effect node; dormant Effect callbacks remain separate.");
        }
        return result.AsReadOnly();
    }

    public static void Apply(WorldCreatureInitialFactory factory, IReadOnlyList<WorldCreatureInitialActor> actors,
        IReadOnlyDictionary<uint, WorldEquipmentInitialItem>? items, double clock)
    {
        foreach (var actor in actors)
        {
            if (!factory.Actors.TryGetValue(actor.Base.ObjectReference, out var current) || !ReferenceEquals(current, actor))
                throw new InvalidOperationException("Sector activation plan no longer owns the same factory actors.");
            // The existing original 640640 projection returns before clock,
            // identity, model/equipment callbacks or insertion when already active.
            if (actor.Node.Active) _ = factory.Activation.ActivateInitial(actor, actor.Node.StartedAt);
            else _ = WorldCreatureSectorReactivation.Activate(actor, factory.Activation, items, clock);
        }
    }
}
