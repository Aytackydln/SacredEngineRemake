using System.Collections.ObjectModel;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FB530 fresh non-hero creature construction through unpublished
/// registration. Input resource/template are already resolved. Owns the initial
/// registry, preserving Node identity across immutable actor updates. Caller
/// placement, assigned level/KI/equipment and serialization remain separate.</summary>
public sealed class WorldCreatureInitialFactory
{
    private readonly Dictionary<uint, WorldCreatureInitialActor> _actors = [];
    private readonly WorldCreatureFactoryRegistration _registration = new();
    private readonly WorldCreatureScalarResultAssets _results;
    private readonly SacredCreatureConstructionDefinitions _definitions;
    private readonly GoldObjectReferencePool _references;
    private readonly WorldObjectAllocationCounter _counter;
    private readonly SacredRandomSequence _random;
    private readonly byte _owner;
    private readonly uint _difficulty;

    public WorldCreatureInitialFactory(WorldCreatureScalarResultAssets results, SacredCreatureConstructionDefinitions definitions,
        GoldObjectReferencePool references, WorldObjectAllocationCounter counter, SacredRandomSequence random,
        byte owner, uint difficulty)
    {
        ArgumentNullException.ThrowIfNull(results);
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(references);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(random);
        if (owner is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(owner));
        (_results, _definitions, _references, _counter, _random, _owner, _difficulty) =
            (results, definitions, references, counter, random, owner, difficulty);
        Actors = new ReadOnlyDictionary<uint, WorldCreatureInitialActor>(_actors);
    }

    public IReadOnlyDictionary<uint, WorldCreatureInitialActor> Actors { get; }
    public WorldObjectActivationQueue Activation { get; } = new();
    public IReadOnlyList<uint> GraphicObjects => _registration.GraphicObjects;

    public WorldCreatureInitialActor Create(SacredCreatureTemplate template, ItemsPakEntry descriptor,
        uint clock, double schedulerClock, bool networking, bool owned)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (descriptor.ItemIndex != template.ItemType || descriptor.ModelDesc.Category != SacredItemCategory.Creature ||
            template.Class == SacredCreatureClass.Hero)
            throw new NotSupportedException("Initial factory requires a resolved non-hero creature descriptor/template.");
        if (!double.IsFinite(schedulerClock) || schedulerClock < 0)
            throw new ArgumentOutOfRangeException(nameof(schedulerClock));
        // Reject an uncompared heading before consuming a reservation or RNG.
        _ = SacredStartupHeading.ToDirection(descriptor.ModelDesc.Angle3D);
        if (!_references.TryAllocate(out var reference))
            throw new InvalidDataException("CreateNPC needs a 0x153 object-reference reservation.");
        var actor = WorldCreatureAllocation.Allocate(reference, _counter, Activation,
            reserved => WorldCreatureConstructor.Create(template, _results, _definitions, _random,
                new(reserved, clock, _difficulty, _owner)),
            (reserved, initial) => _actors.Add(reserved, initial), schedulerClock)
            ?? throw new InvalidOperationException("Non-hero creature construction returned no actor.");
        actor = WorldCreatureFactoryIdentity.Assign(actor, descriptor, reference, _owner, actor.Base.Byte38, owned);
        actor = _registration.CompleteUnpublished(actor, descriptor, networking, newReference: true);
        Update(actor);
        return actor;
    }

    public void Update(WorldCreatureInitialActor actor)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var reference = actor.Base.ObjectReference;
        if (!_actors.TryGetValue(reference, out var current) || !ReferenceEquals(current.Node, actor.Node))
            throw new InvalidOperationException("Actor update does not belong to this initial registry lifetime.");
        _actors[reference] = actor;
    }
}
