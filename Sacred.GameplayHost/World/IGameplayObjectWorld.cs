using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;
using Sacred.World.Objects;

namespace Sacred.GameplayHost.World;

/// <summary>Gameplay owns object identity, placement, trigger state and interaction effects.
/// Renderers create only their own graphics resources from these instances.</summary>
public interface IGameplayObjectWorld : IDisposable
{
    bool IsNetwork { get; }
    bool IsCurrentSession { get; }
    long Revision { get; }
    IReadOnlyDictionary<uint, GameplayWorldObject> Objects { get; }
    IReadOnlyDictionary<uint, GoldWorldTriggerState> Triggers { get; }
    WorldDoorStateLayer DoorStates { get; }
    string InteractionStatus { get; }
    void Update(VisibleWorld visibleWorld, IndoorTileGroup? indoorGroup);
    /// <summary>Advance presentation once per frame, independently of visibility reconciliation.</summary>
    void Advance(float seconds) { }
    bool RequestInteraction(uint reference, GameplayInteractionContext context);
}
