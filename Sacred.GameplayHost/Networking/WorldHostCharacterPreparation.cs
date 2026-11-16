using Sacred.Network.Gameplay;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.GameplayHost.Networking;

/// <summary>Private saved-character import using this host's actual reservations.
/// The saved pose and retained values are not the native campaign entry pose or
/// a restored/registered runtime actor. No packet is sent or replica seeded.</summary>
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
    public string ActorBindingBlocker => "Native pre-StartCode saved-hero import/model activation, entry-position application and live actor binding are pending; the private initial factory actor cannot grant readiness.";
}
