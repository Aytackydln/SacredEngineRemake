namespace Sacred.GameplayHost.Networking;

/// <summary>Observed identities after the local service returns. Absence of a
/// current connection is retained, rather than inventing a zero local reference.</summary>
public sealed record WorldController117Identity(uint LocalReference, uint HostedServerWindow, uint? ConnectionLocalReference);

/// <summary>Post-service diagnostic state, distinct from the earlier prepared
/// admission projection. This never proves registration, binding or readiness.</summary>
public sealed record WorldController117Snapshot(WorldController117Identity Identity, WorldAdmissionControllerGates Gates,
    WorldAdmissionControllerServiceFlags ServiceFlags, byte BusyByte8, byte HostedProcessActive1C,
    uint HostedProcessHandle20, byte Flag49)
{
    public byte HostByte1 { get; init; }
    public byte PendingByte3 { get; init; }
}
