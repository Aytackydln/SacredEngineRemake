using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>Gold 0x11 dispatch -> 0x7E1257..0x7E13A5 field writes. Query result
/// and controller +9 are established caller inputs; no UI/send or lifetime is
/// inferred. This does not replace the existing admission tracker.</summary>
public static class WorldAdmissionControllerPrefix
{
    public static WorldAdmissionControllerPrefixResult Apply(WorldAdmissionControllerState state,
        GoldAdmissionEvent admission, bool connectionQuery, bool controllerFlag9)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(admission);
        var decoded = GoldGameEventDecoder.Decode(admission.Message) as GoldAdmissionEvent
            ?? throw new InvalidDataException("Controller admission prefix requires a typed 0x11 response.");
        var next = state with { PendingByte3 = 0 };
        if (connectionQuery && controllerFlag9 && (decoded.Flags & 1) == 0)
            return new(next with { LocalReference = 0, ConnectionLocalReference = 0 }, true);
        return new(next with
        {
            LocalReference = decoded.PlayerSlot,
            ConnectionLocalReference = decoded.PlayerSlot,
            HostByte1 = (decoded.Flags & 1) != 0 ? (byte)1 : state.HostByte1,
            HostToken = decoded.HostToken != 0 ? decoded.HostToken : state.HostToken
        }, false);
    }
}
