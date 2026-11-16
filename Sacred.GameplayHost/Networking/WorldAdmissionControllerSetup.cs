using Sacred.Network.Admission;

namespace Sacred.GameplayHost.Networking;

/// <summary>Compared admission-field projection of complete 0x7D7A70 construction
/// and 0x7D8560 initialization under their supplied service profiles. Platform,
/// credential/profile, storage and connection service internals remain external.</summary>
public static class WorldAdmissionControllerSetup
{
    public static WorldAdmissionControllerSnapshot Construct(uint existingConnectionLocalReference) =>
        new(new(0, 0, 0, 0, existingConnectionLocalReference), EmptyRoster(), new(0, 0, 0, 0), new(0, 0, 0, 0, 0, 0), null);

    public static WorldAdmissionControllerSnapshot Initialize(WorldAdmissionControllerSnapshot previous,
        byte mode3818, byte connectionResult, uint newConnectionLocalReference)
    {
        ArgumentNullException.ThrowIfNull(previous);
        return new(new(0, 0, 0, 0, newConnectionLocalReference), EmptyRoster(), new(connectionResult, 0, 0, 0),
            previous.ServiceFlags with { Byte3818 = mode3818, Byte381D = 0 }, connectionResult);
    }

    private static GoldPlayerRoster EmptyRoster() => GoldPlayerRoster.FromPayload(new byte[GoldPlayerRosterLayout.Size]);
}
