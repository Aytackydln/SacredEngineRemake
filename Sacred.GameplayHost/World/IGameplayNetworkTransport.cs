using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.World;

/// <summary>Connection capability only; object selection and gameplay requests belong to GameplayHost.</summary>
public interface IGameplayNetworkTransport
{
    byte PlayerSlot { get; }
    bool IsParticipantWorldApproved { get; }
    Task SendGameplayAsync(GoldMessage message);
}
