using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.World;

/// <summary>Original protocol reducers provide the gameplay state shared by every presentation.</summary>
public sealed class SacredGameplayReplicaSession : IGameplayReplicaSession
{
    public GoldWorldReplica Replicas { get; } = new();
    public void Apply(GoldGameEvent gameEvent) => Replicas.Apply(gameEvent);
    public void SeedCharacter(GoldParticipantCharacterUpload character) => Replicas.SeedCharacter(character);
    public void Clear() => Replicas.Clear();
}
