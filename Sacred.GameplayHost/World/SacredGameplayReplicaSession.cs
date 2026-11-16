using Sacred.Network.Gameplay;
using Sacred.GameplayHost.Networking;

namespace Sacred.GameplayHost.World;

/// <summary>Original protocol reducers provide the gameplay state shared by every presentation.</summary>
public sealed class SacredGameplayReplicaSession : IGameplayReplicaSession
{
    public SacredGameplayReplicaSession() => Admission = new(Replicas);
    public GoldWorldReplica Replicas { get; } = new();
    public WorldAdmissionSession Admission { get; }
    public void Apply(GoldGameEvent gameEvent)
    {
        var entry = WorldAdmissionSession.Validate(gameEvent);
        Replicas.Apply(entry);
        Admission.Observe(entry);
    }
    public void SeedCharacter(GoldParticipantCharacterUpload character) => Replicas.SeedCharacter(character);
    public void Clear()
    {
        Admission.Clear();
        Replicas.Clear();
    }
}
