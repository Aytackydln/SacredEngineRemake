using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Gameplay entry point for decoded object, trigger and world events.
/// Connection owners deliver messages; gameplay owns replica creation and session lifetime.</summary>
public interface IGameplayReplicaSession
{
    GoldWorldReplica Replicas { get; }
    void Apply(GoldGameEvent gameEvent);
    void SeedCharacter(GoldParticipantCharacterUpload character);
    void Clear();
}
