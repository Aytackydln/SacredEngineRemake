using Sacred.Network.Gameplay;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.GameplayHost.Networking;

/// <summary>Private saved-character import using this host's actual reservations.
/// Owns the private factory reader stages and retained packet graph. The saved
/// pose is distinct from native campaign entry; post-load actions, equipment and
/// placement still gate live actor registration. No packet is sent or replica seeded.</summary>
public sealed class WorldHostCharacterPreparation
{
    private readonly Func<bool> _isCurrent;
    internal WorldHostCharacterPreparation(WorldHostSavedCharacterSource source, byte slot,
        GoldObjectReferencePool references, uint gameFlags, ushort levelMode, Func<bool> isCurrent)
    {
        Source = source;
        _isCurrent = isCurrent;
        EntryPosition = source.StartPositions.Select(slot, GoldSessionGameFlagsCodec.IsCampaignGame(gameFlags), levelMode);
        var allocated = references.PeekAvailable(source.ObjectCount);
        ReferencePlan = GoldSavedCharacterReferencePlan.Create(source.Records, slot, allocated);
        Import = GoldSavedCharacterImportPreparation.Create(ReferencePlan);
        Packets = GoldSavedCharacterPacketPreparation.Create(Import);
        HeroConstruction = WorldHostHeroConstruction.Prepare(source, slot, GoldSessionGameFlagsCodec.Difficulty(gameFlags), references);
        AnimationRequest = SacredSavedAnimationRequest.Read(source.Records.Hero.Creature.PhysicalData);
        // Validate the complete plan/import/packet graph before draining any IDs.
        references.Consume(allocated);
    }
    public WorldHostSavedCharacterSource Source { get; }
    public GoldSavedCharacterReferencePlan ReferencePlan { get; }
    public GoldSavedCharacterImportPreparation Import { get; }
    public GoldParticipantCharacterUpload Packets { get; }
    public WorldHostHeroConstruction HeroConstruction { get; }
    public SacredSavedAnimationRequest AnimationRequest { get; }
    /// <summary>Authored position selected from original MultiStart.bin. Actor
    /// placement must still resolve/apply it; its raw sector may be zero.</summary>
    public SacredMultiplayerStartPositionLayout EntryPosition { get; }
    public bool IsCurrent => _isCurrent();
    public string ActorBindingBlocker => HeroConstruction.Actor.RestoredPhysical is null
        ? "Native pre-StartCode saved-hero physical/model import is pending; the private initial factory actor cannot grant readiness."
        : HeroConstruction.Actor.RestoredCreature is null
            ? "Host physical import completed; saved creature scalar/art/combo/status normalization remains pending before inventory and placement."
            : !HeroConstruction.Actor.HeroPostLoadCompleted
                ? "Host hero file import completed; C0 post-load actions remain pending before saved object construction."
            : !HeroConstruction.SavedItemsRestored
                ? "Host hero physical/value imports completed; actual saved object construction remains pending before equipment and placement."
            : !HeroConstruction.SavedItemPostLoadCompleted
                ? "C2 saved item damage/price normalization remains pending before reference and inventory binding."
            : HeroConstruction.InventoryBinding is null
                ? "C2 item normalization completed; actual equipment references and inventories remain unbound."
            : !HeroConstruction.Actor.SavedWorldPreludeCompleted
                ? "Saved-position world loading, actual hero reinsertion and surface actions remain pending before body reactivation."
            : !HeroConstruction.Actor.ModelReactivated
                ? "C0/C2 reference graphs completed; original outer-loader body model reactivation remains pending."
            : !HeroConstruction.Actor.SavedLoaderTailPrepared
                ? "Original body reactivation completed with a real bind pose; final saved equipment/inventory/heading preparation remains pending."
            : !HeroConstruction.Actor.CampaignEntryPlaced
                ? "Saved world/body/final-loader preparation completed; original campaign inventory quest reset, retirement and placement remain pending."
            : HeroConstruction.RuntimePublication is null
                ? "Native entered actor requires current-state serialization and value snapshots before publication."
                : "Current actor/item/inventory export verified on the original entered graph; session local-hero binding and shared startup inputs/callbacks still guard StartCode.";
}
