using System;
using System.Threading.Tasks;
using Sacred.Network.Gameplay;

namespace Sacred.Engine.Multiplayer;

internal sealed partial class GameServerClientController
{
    public Task RequestWorldBanksAsync()
    {
        if (!IsParticipantWorldApproved)
            throw new InvalidOperationException("World-bank request requires native participant approval.");
        EngineLog.WriteLine("Network participant: requesting current native script/world banks (0x1B5).");
        return _connection!.SendAsync(GoldWorldEntryCodec.CreateWorldBankRequest(PlayerSlot)).AsTask();
    }
}
