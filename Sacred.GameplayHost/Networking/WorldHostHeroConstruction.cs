using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Private initial hero in the existing creature factory, using the
/// actual admitted local slot independently of ordinary object grants. The
/// registered/active node is still unplaced and has no restored model/save.
/// Initial art/combo container retention is deferred to saved import; only their
/// constructor scalar effects are compared here. This cannot grant readiness.</summary>
public sealed class WorldHostHeroConstruction
{
    private WorldHostHeroConstruction(SacredCreatureTemplate template, WorldCreatureInitialActor actor,
        uint seed, uint clock, WorldCreatureInitialFactory factory, WorldObjectAllocationCounter counter)
        => (Template, Actor, RandomSeed, Clock, Factory, Counter) = (template, actor, seed, clock, factory, counter);
    internal WorldCreatureInitialFactory Factory { get; }
    private ItemsPakEntry Descriptor { get; init; }
    public WorldHostHeroModelResources? ModelResources { get; private set; }
    public WorldObjectAllocationCounter Counter { get; }
    public SacredCreatureTemplate Template { get; }
    public WorldCreatureInitialActor Actor { get; }
    public uint RandomSeed { get; }
    public uint Clock { get; }
    public bool IsInvalidated { get; private set; }
    internal void Invalidate()
    {
        ModelResources?.Invalidate();
        Factory.DiscardInitialHero(Actor);
        IsInvalidated = true;
    }

    internal async Task PrepareModelAsync(string installation, SacredSavedAnimationRequest request,
        Func<bool> isCurrent, CancellationToken cancellationToken)
    {
        if (!isCurrent() || IsInvalidated) throw new InvalidOperationException("Host hero model preparation requires the current factory lifetime.");
        if (ModelResources is not null || !request.RequiresActivation) return;
        var resources = await WorldHostHeroModelResources.LoadAsync(installation, Descriptor, request, cancellationToken).ConfigureAwait(false);
        if (!isCurrent() || IsInvalidated) throw new InvalidOperationException("Host hero lifetime ended while preparing model resources.");
        ModelResources = resources;
    }

    internal static WorldHostHeroConstruction Prepare(WorldHostSavedCharacterSource source, byte slot, uint difficulty, GoldObjectReferencePool references)
    {
        var resource = source.Records.Hero.Creature.BaseState.Resource;
        var items = SacredObjectTemplateArchive.Load(source.Installation);
        var creatures = CreaturePakArchive.Load(Path.Combine(source.Installation, "pak", "Creature.pak"));
        var template = creatures.ResolveTemplate(items.GetItem(resource));
        if (template is null || template.Class != SacredCreatureClass.Hero || template.ItemType != resource)
            throw new InvalidDataException("Selected host hero needs its original matching hero template.");
        var clock = unchecked((uint)Environment.TickCount);
        var seed = clock;
        var counter = new WorldObjectAllocationCounter();
        var factory = new WorldCreatureInitialFactory(WorldCreatureScalarResultAssets.Load(source.Installation),
            SacredCreatureConstructionDefinitions.Load(source.Installation), references, counter, new SacredRandomSequence(seed), slot, difficulty);
        var actor = factory.CreateHero(template, items.GetItem(resource), clock, clock / 1000.0,
            SacredEquipmentDefaultDefinitions.Load(source.Installation));
        return new(template, actor, seed, clock, factory, counter) { Descriptor = items.GetItem(resource) };
    }
}
