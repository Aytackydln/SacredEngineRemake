using System;
using System.Threading.Tasks;
using Sacred.GameplayHost.World;
using Sacred.Network.Protocol;

namespace Sacred.Engine.Multiplayer;

/// <summary>Transport adapter. GameplayHost constructs and validates object interaction commands.</summary>
internal sealed partial class GameServerClientController : IGameplayNetworkTransport
{
    Task IGameplayNetworkTransport.SendGameplayAsync(GoldMessage message)
    {
        if (!IsParticipantWorldApproved || _connection is null || message.Sender != PlayerSlot || message.Origin != PlayerSlot)
            throw new InvalidOperationException("Gameplay send requires the active approved participant identity.");
        return _connection.SendAsync(message).AsTask();
    }
}
