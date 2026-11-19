using System.Collections.ObjectModel;
using Sacred.Assets.Executable;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Runtime;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x5FB530 fresh creature construction, including the guarded admitted hero, through unpublished
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
    internal SacredRandomSequence Random => _random;
    internal uint Difficulty => _difficulty;
    public WorldObjectActivationQueue Activation { get; } = new();
    public IReadOnlyList<uint> GraphicObjects => _registration.GraphicObjects;

    /// <summary>Original constructor first seeds the admitted local slot through
    /// 600F80 before receiving ordinary object grants. This slot is not a 153 ID.
    /// Registration/activation precede identity. Saved import/placement still follow.</summary>
    public WorldCreatureInitialActor CreateHero(SacredCreatureTemplate template, ItemsPakEntry descriptor,
        uint clock, double schedulerClock, SacredEquipmentDefaultDefinitions defaults)
    {
        if (template.Class != SacredCreatureClass.Hero || descriptor.ItemIndex != template.ItemType ||
            descriptor.ModelDesc.Category != SacredItemCategory.Creature || _actors.ContainsKey(_owner))
            throw new InvalidOperationException("Admitted hero factory requires a matching original hero and an unused local slot.");
        // The native constructor detaches all initial slots. Some classes create
        // original default items during that operation, before hero registration.
        // Do not accept the empty-descriptor failure profile as real equipment.
        for (ushort slot = 0; slot < 19; slot++)
            if (defaults.Resource(template.ItemType, slot) != 0)
                throw new NotSupportedException("Hero construction requires its original default-item factory before registration.");
        var scriptBase = _definitions.HeroScriptResourceBase ?? throw new InvalidDataException("Original hero factory identity operand unavailable.");
        _ = SacredStartupHeading.ToDirection(descriptor.ModelDesc.Angle3D);
        if (!double.IsFinite(schedulerClock) || schedulerClock < 0) throw new ArgumentOutOfRangeException(nameof(schedulerClock));
        _counter.BeginAllocation();
        var actor = WorldCreatureConstructor.Create(template, _results, _definitions, _random,
            new(_owner, clock, _difficulty, _owner));
        actor = actor with { Base = actor.Base with { Byte38 = _counter.Value } };
        _actors.Add(_owner, actor);
        Activation.ActivateInitial(actor, schedulerClock);
        actor = WorldCreatureFactoryIdentity.Assign(actor, descriptor, _owner, _owner, actor.Base.Byte38, owned: false);
        actor = actor with { Base = actor.Base with { Word3C = checked(scriptBase + _owner) } };
        actor = _registration.CompleteUnpublished(actor, descriptor, networking: true, newReference: true);
        Update(actor);
        return actor;
    }

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

    internal void DiscardInitialHero(WorldCreatureInitialActor actor)
    {
        if (!_actors.TryGetValue(actor.Base.ObjectReference, out var current) || !ReferenceEquals(current.Node, actor.Node)) return;
        Activation.DeactivateInitial(actor.Node);
        _actors.Remove(actor.Base.ObjectReference);
    }

    /// <summary>End this owner lifetime, including actors created by shared
    /// StartCode and every node in its shared activation queue. This is managed
    /// session invalidation, not a claim of native gameplay destructor effects.</summary>
    internal void DiscardWorld()
    {
        foreach (var node in Activation.Nodes.ToArray()) Activation.DeactivateInitial(node);
        _actors.Clear();
        _registration.Clear();
    }
}
