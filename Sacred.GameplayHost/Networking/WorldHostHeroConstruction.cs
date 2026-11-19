using Sacred.Assets.Executable;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.Pak.Creature;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pax.Hero.Objects;
using Sacred.Core.Runtime;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;
using Sacred.Assets.GameBin.Balance;
using Sacred.Assets.Equipment;
using Sacred.GameplayHost.Equipment;
using Sacred.Granny.Animation;

namespace Sacred.GameplayHost.Networking;

/// <summary>Private initial hero in the existing creature factory, using the
/// actual admitted local slot independently of ordinary object grants. The
/// registered/active node retains its physical import and actual pose backend;
/// saved creature normalization replaces constructor values/lists in place.
/// Inventory and campaign entry placement remain separate. This cannot grant readiness.</summary>
public sealed class WorldHostHeroConstruction
{
    private WorldHostHeroConstruction(SacredCreatureTemplate template, WorldCreatureInitialActor actor,
        uint seed, uint clock, WorldCreatureInitialFactory factory, WorldObjectAllocationCounter counter)
        => (Template, Actor, RandomSeed, Clock, Factory, Counter) = (template, actor, seed, clock, factory, counter);
    internal WorldCreatureInitialFactory Factory { get; }
    private ItemsPakEntry Descriptor { get; init; }
    private WorldCreatureScalarResultAssets ScalarResults { get; init; } = null!;
    private uint Difficulty { get; init; }
    private WorldHostHeroValueAssets? _valueAssets;
    private SacredEquipmentGenerationData? _equipmentAssets;
    public WorldHostHeroModelResources? ModelResources { get; private set; }
    public WorldHostHeroAnimation? Animation { get; private set; }
    public WorldHostHeroBindPose? BindPose { get; private set; }
    public GrnPose? CurrentPose => BindPose?.Pose ?? Animation?.Pose;
    public WorldHostCharacterTerrain? Terrain { get; private set; }
    public WorldHostHeroValueSnapshots? ValueSnapshots { get; private set; }
    public GoldParticipantCharacterUpload? RuntimePublication { get; private set; }
    public WorldObjectAllocationCounter Counter { get; }
    public WorldEquipmentInitialFactory EquipmentFactory { get; private init; } = null!;
    public bool SavedItemsRestored { get; private set; }
    public bool SavedItemPostLoadCompleted { get; private set; }
    public WorldHostSavedInventoryBinding? InventoryBinding { get; private set; }
    private GoldObjectReferencePool References { get; init; } = null!;
    public SacredCreatureTemplate Template { get; }
    public WorldCreatureInitialActor Actor { get; private set; }
    public uint RandomSeed { get; }
    public uint Clock { get; }
    public bool IsInvalidated { get; private set; }
    internal void Invalidate()
    {
        ModelResources?.Invalidate();
        Animation?.Invalidate();
        BindPose?.Invalidate();
        Terrain?.Invalidate();
        EquipmentFactory.DiscardSavedItems();
        InventoryBinding = null;
        ValueSnapshots = null;
        RuntimePublication = null;
        Factory.DiscardWorld();
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

    internal void RestorePhysical(string installation, SacredSavedCreature saved)
    {
        if (IsInvalidated) throw new InvalidOperationException("Host physical import requires the current factory lifetime.");
        if (Actor.RestoredPhysical is not null) return;
        var request = SacredSavedAnimationRequest.Read(saved.PhysicalData);
        if (request.RequiresActivation && ModelResources is not { IsInvalidated: false })
            throw new InvalidOperationException("Host physical activation requires its original loaded model and saved numeric motion.");
        var actor = WorldCreatureSavedPhysicalImport.Restore(Actor, saved.PhysicalData,
            ModelResources?.RootLocalOffset, ModelResources?.SavedAnimation.PlaybackSpeedMultiplier ?? 1,
            BalanceBinArchive.Load(Path.Combine(installation, "bin", "Balance.bin")).MovementScale,
            WorldStartupEffectProjection.ComparedGold);
        var physical = actor.RestoredPhysical!;
        var animation = request.RequiresActivation ? new WorldHostHeroAnimation(ModelResources!, physical.LoopCount, physical.PlaybackSpeed) : null;
        // All native rules and the real pose backend must succeed before the
        // existing factory sees the restored physical root. No IDs are allocated.
        Factory.Update(actor);
        Actor = actor;
        Animation = animation;
    }

    internal void RestoreCreature(string installation, SacredSavedCreature saved)
    {
        if (IsInvalidated) throw new InvalidOperationException("Host creature import requires the current factory lifetime.");
        if (Actor.RestoredCreature is not null) return;
        var assets = _valueAssets ??= WorldHostHeroValueAssets.Load(installation, saved);
        var actor = WorldCreatureSavedValueImport.Restore(Actor, saved, Template, ScalarResults, assets.Profiles, assets.Arts, Difficulty);
        Factory.Update(actor);
        Actor = actor;
    }

    internal void RestoreSavedItems(string installation, GoldSavedCharacterReferencePlan plan)
    {
        if (IsInvalidated || !Actor.HeroPostLoadCompleted)
            throw new InvalidOperationException("Saved items require the current restored hero lifetime.");
        if (SavedItemsRestored) return;
        var data = _equipmentAssets ??= SacredEquipmentGenerationData.Load(installation);
        WorldEquipmentSavedCharacterImport.Restore(plan, data,
            References, EquipmentFactory, Clock / 1000.0);
        SavedItemsRestored = true;
    }

    internal void CompleteSavedItemPostLoad()
    {
        if (IsInvalidated || !SavedItemsRestored || _equipmentAssets is null)
            throw new InvalidOperationException("C2 item normalization requires this lifetime's actual saved objects.");
        if (SavedItemPostLoadCompleted) return;
        WorldEquipmentSavedPostLoad.Complete(EquipmentFactory, _equipmentAssets);
        SavedItemPostLoadCompleted = true;
    }

    internal void BindSavedEquipmentAndInventories(GoldSavedCharacterReferencePlan plan)
    {
        if (IsInvalidated || !SavedItemPostLoadCompleted || _equipmentAssets?.EquipmentDefaults is not { } defaults)
            throw new InvalidOperationException("C2 binding requires this lifetime's normalized objects and original default definitions.");
        if (InventoryBinding is not null) return;
        if (_valueAssets is null || _valueAssets.InventoryMode.RequiresTransition(Actor.Base.ObjectType))
            throw new NotSupportedException("C2 requires its original separate inventory-mode action before this actor can bind.");
        if (EquipmentFactory.Items.Values.Any(item => (item.Physical.Base.Flags & 2) == 0))
            throw new NotSupportedException("C2 placed-item retirement requires its original world-manager effects before binding.");
        var index = 0;
        foreach (var record in plan.Source.Objects.Records)
        {
            if (index > ushort.MaxValue || record.IsEmpty && defaults.Resource(Actor.Base.ObjectType, (ushort)index) != 0)
                throw new NotSupportedException("C2 empty slot requires its original default-item detach/create action before binding.");
            index++;
        }
        var actor = WorldCreatureSavedEquipmentReferences.Bind(Actor, plan.References, EquipmentFactory.Items);
        var inventories = WorldHostSavedInventoryBinding.Prepare(plan, actor, EquipmentFactory.Items);
        // Both graphs validate before any current actor/item state is changed.
        Factory.Update(actor);
        inventories.Commit(EquipmentFactory.Items);
        Actor = actor;
        InventoryBinding = inventories;
    }

    internal void ReactivateBodyModel()
    {
        if (IsInvalidated || InventoryBinding is null || ModelResources is not { IsInvalidated: false } resources)
            throw new InvalidOperationException("Body reactivation requires the current real body and bound inventory lifetime.");
        if (Actor.ModelReactivated) return;
        var actor = WorldCreatureHeroModelReactivation.Prepare(Actor, EquipmentFactory.Items, resources.RootLocalOffset,
            WorldStartupEffectProjection.ComparedGold);
        var pose = new WorldHostHeroBindPose(resources);
        Factory.Update(actor);
        WorldCreatureHeroModelReactivation.CompleteUnloadedSlots(actor, EquipmentFactory.Items);
        Animation?.Invalidate();
        Animation = null;
        BindPose = pose;
        Actor = actor;
    }

    internal void ApplyWorldActor(WorldCreatureInitialActor actor)
    {
        if (IsInvalidated || !ReferenceEquals(actor.Node, Actor.Node) || actor.Base.ObjectReference != Actor.Base.ObjectReference)
            throw new InvalidOperationException("Host world effects require the existing current actor/node.");
        Factory.Update(actor);
        Actor = actor;
    }

    internal void CompleteSavedWorldPrelude(string installation)
    {
        if (IsInvalidated || InventoryBinding is null) throw new InvalidOperationException("Saved-world prelude requires current bound inventories.");
        if (Actor.SavedWorldPreludeCompleted) return;
        ApplyWorldActor(WorldCreatureHostWorldPrelude.BeginFreshWorld(Actor));
        Terrain ??= new(installation, this);
        Terrain.ResolveSavedSurface();
        ApplyWorldActor(WorldCreatureHostWorldPrelude.CompleteOutdoor(Actor));
    }

    internal void PlaceCampaignEntry(Sacred.Core.GameBin.Scripts.SacredMultiplayerStartPositionLayout entry)
    {
        if (IsInvalidated || !Actor.SavedWorldPreludeCompleted || !Actor.SavedLoaderTailPrepared || Terrain is null || InventoryBinding is null)
            throw new InvalidOperationException("Campaign entry requires the current complete saved actor/world preparation.");
        if (Actor.CampaignEntryPlaced) return;
        ValueSnapshots ??= new(Actor);
        // Engine parameter1D clears only primary inventory quest records.
        InventoryBinding = InventoryBinding.ClearPrimaryQuests();
        Terrain.PlaceEntry(new(entry.X, entry.Y, entry.Surface, entry.Sector));
    }

    internal void PrepareRuntimePublication(GoldSavedCharacterReferencePlan plan)
    {
        if (IsInvalidated || !Actor.CampaignEntryPlaced || ValueSnapshots is null || InventoryBinding is null || _equipmentAssets is null || _valueAssets is null)
            throw new InvalidOperationException("Current-state publication requires the same entered actor, snapshots, inventory and original definitions.");
        if (RuntimePublication is not null) return;
        ValueSnapshots.RequireMatch(Actor);
        // Engine captures current position at 60BCA9 and repeats the same native
        // script identity assigned by the original hero factory.
        var actor = Actor with { ScriptInitialPosition = new(Actor.Base.X, Actor.Base.Y, Actor.Base.Surface, Actor.Base.Sector),
            Base = Actor.Base with { Flags = Actor.Base.Flags & ~0x10000u } };
        var hero = WorldCreatureHostSerialization.Write(actor, EquipmentFactory.Items, _equipmentAssets.Templates.GetItem, _valueAssets.Publication.HeroMode);
        var states = new List<byte[]>();
        foreach (var record in plan.Source.Objects.Records)
            if (!record.IsEmpty)
                states.Add(WorldEquipmentSavedSerialization.Write(EquipmentFactory.Items[plan.References[record.BaseState.ObjectReference]],
                    _equipmentAssets.Templates.GetItem(record.BaseState.Resource)).ToPayload());
        var publication = GoldParticipantCharacterUpload.Create(plan, states, hero.ToPayload(), InventoryBinding.Primary.Inventory, InventoryBinding.Shared.Inventory);
        ApplyWorldActor(actor);
        foreach (var item in EquipmentFactory.Items.Values)
            item.Physical.Base = item.Physical.Base with { Flags = item.Physical.Base.Flags & ~0x10000u };
        RuntimePublication = publication;
    }

    internal void CompleteHeroPostLoad()
    {
        if (IsInvalidated) throw new InvalidOperationException("C0 post-load requires the current hero lifetime.");
        if (Actor.HeroPostLoadCompleted) return;
        if (_valueAssets is null || EquipmentFactory.Items.Count != 0)
            throw new InvalidOperationException("C0 post-load must precede saved item construction on this restored factory lifetime.");
        var actor = WorldCreatureHeroPostLoad.Apply(Actor, Template, ScalarResults, _valueAssets.Arts, _valueAssets.PostLoad, Difficulty);
        Factory.Update(actor);
        Actor = actor;
    }

    internal void PrepareSavedLoaderTail()
    {
        if (IsInvalidated || InventoryBinding is null || _equipmentAssets?.EquipmentDefaults is not { } defaults)
            throw new InvalidOperationException("Saved-loader tail requires the current actual equipment/inventory lifetime and original definitions.");
        if (Actor.SavedLoaderTailPrepared) return;
        var actor = WorldCreatureSavedLoaderTail.Prepare(Actor, EquipmentFactory.Items, Descriptor,
            _equipmentAssets.Templates.GetItem, defaults.Resource, WorldStartupEffectProjection.ComparedGold);
        var inventories = InventoryBinding.Refix(actor, EquipmentFactory.Items);
        // All slot, model, heading and inventory rules validate before commit.
        WorldCreatureSavedLoaderTail.CommitItems(actor, EquipmentFactory.Items);
        inventories.Commit(EquipmentFactory.Items);
        Factory.Update(actor);
        Actor = actor;
        InventoryBinding = inventories;
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
        var scalarResults = WorldCreatureScalarResultAssets.Load(source.Installation);
        var factory = new WorldCreatureInitialFactory(scalarResults,
            SacredCreatureConstructionDefinitions.Load(source.Installation), references, counter, new SacredRandomSequence(seed), slot, difficulty);
        var actor = factory.CreateHero(template, items.GetItem(resource), clock, clock / 1000.0,
            SacredEquipmentDefaultDefinitions.Load(source.Installation));
        return new(template, actor, seed, clock, factory, counter)
        {
            Descriptor = items.GetItem(resource), ScalarResults = scalarResults, Difficulty = difficulty, References = references,
            EquipmentFactory = new(references, counter, factory.Activation, slot)
        };
    }
}
