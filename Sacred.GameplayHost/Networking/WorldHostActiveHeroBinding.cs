using System.Buffers.Binary;
using Sacred.Core.Pak.Creature;
using Sacred.GameplayHost.Equipment;
using Sacred.GameplayHost.Scripting;
using Sacred.Granny.Animation;
using Sacred.Core.Runtime;

namespace Sacred.GameplayHost.Networking;

/// <summary>The entered local hero in this receipt's existing factory, with its
/// actual CPU model/pose and terrain lifetime. This owns no registry, replicas,
/// grant pool or readiness. Startup inputs must consume this current graph.</summary>
public sealed class WorldHostActiveHeroBinding
{
    private readonly WorldHostCharacterPreparation _character;
    private readonly WorldObjectNode _node;
    private readonly byte _slot;

    internal WorldHostActiveHeroBinding(WorldHostCharacterPreparation character)
    {
        _character = character;
        _node = character.HeroConstruction.Actor.Node;
        _slot = character.ReferencePlan.PlayerSlot;
        RequireCurrent();
    }

    public WorldCreatureInitialActor Actor => Resolve();
    public ushort Level => BinaryPrimitives.ReadUInt16LittleEndian(Actor.Component.Values.AsSpan(78));
    public uint RandomState { get { RequireCurrent(); return Hero.Factory.Random.State; } }
    public WorldHostHeroModelResources Models { get { RequireCurrent(); return Hero.ModelResources!; } }
    public GrnPose Pose { get { RequireCurrent(); return Hero.CurrentPose!; } }
    public WorldHostCharacterTerrain Terrain { get { RequireCurrent(); return Hero.Terrain!; } }
    public WorldObjectAllocationCounter Counter { get { RequireCurrent(); return Hero.Counter; } }
    public IReadOnlyDictionary<uint, WorldCreatureInitialActor> Creatures { get { RequireCurrent(); return Hero.Factory.Actors; } }
    public IReadOnlyDictionary<uint, WorldEquipmentInitialItem> Equipment { get { RequireCurrent(); return Hero.EquipmentFactory.Items; } }
    internal WorldCreatureInitialFactory Factory { get { RequireCurrent(); return Hero.Factory; } }
    internal WorldEquipmentInitialFactory EquipmentFactory { get { RequireCurrent(); return Hero.EquipmentFactory; } }
    internal SacredRandomSequence Random { get { RequireCurrent(); return Hero.Factory.Random; } }
    internal string Installation => _character.Source.Installation;
    internal void UpdateActor(WorldCreatureInitialActor actor) { RequireCurrent(); Hero.ApplyWorldActor(actor); }
    private WorldHostHeroConstruction Hero => _character.HeroConstruction;

    public string StartupBlocker => "Entered local factory hero/model/pose bound; StartCode uses this graph with an explicit fresh-world request. Required native active-hero inputs/callbacks, full world upload, approval and registered network actor/readiness remain guarded.";

    public void RequireCurrent() => _ = Resolve();

    private WorldCreatureInitialActor Resolve()
    {
        if (!_character.IsCurrent || Hero.IsInvalidated || !Hero.Factory.Actors.TryGetValue(_slot, out var actor) ||
            !ReferenceEquals(actor, Hero.Actor) || !ReferenceEquals(actor.Node, _node) || !_node.Active ||
            actor.Base.ObjectReference != _slot || actor.Base.Owner != _slot ||
            Hero.Template.Class != SacredCreatureClass.Hero || actor.RelationClass != (uint)SacredCreatureClass.Hero ||
            actor.Base.ObjectType != Hero.Template.ItemType || !actor.CampaignEntryPlaced ||
            !actor.SavedWorldPreludeCompleted || !actor.SavedLoaderTailPrepared || !actor.ModelReactivated ||
            Hero.RuntimePublication is null || Hero.ValueSnapshots is null || Hero.InventoryBinding is null ||
            Hero.ModelResources is not { IsInvalidated: false } || Hero.CurrentPose is not { IsEvaluated: true } ||
            Hero.Terrain is not { IsInvalidated: false })
            throw new InvalidOperationException("Active host hero requires this receipt's current entered factory actor, node, model/pose, inventories and terrain.");
        Hero.ValueSnapshots.RequireMatch(actor);
        return actor;
    }
}
