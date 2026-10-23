using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>The native client's empty 0x0B request/pulse, distinct from the server's 508-byte snapshot.
/// It also reaches GameServer's gameplay activity tracking, which TinCat staying-alive frames do not.</summary>
public static class GoldWorldEnvironmentCodec
{
    public static GoldMessage CreateRequest(byte playerSlot)
    {
        if (playerSlot is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(playerSlot));
        return new(GoldWorldEnvironmentLayout.Opcode, playerSlot, playerSlot, 0, []);
    }
}
