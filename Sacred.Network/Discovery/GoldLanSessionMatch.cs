using System.Net;

namespace Sacred.Network.Discovery;

/// <summary>Resolve the advertised game selected by a direct TCP connection, including a local loopback alias.</summary>
public static class GoldLanSessionMatch
{
    public static GoldLanAnnouncement? Find(IPEndPoint remote, IEnumerable<GoldLanAnnouncement> servers,
        IReadOnlySet<IPAddress> localAddresses)
    {
        var address = remote.Address.IsIPv4MappedToIPv6 ? remote.Address.MapToIPv4() : remote.Address;
        var matches = servers.Where(server => server.Endpoint.Port == remote.Port &&
            (server.Endpoint.Address.Equals(address) || IPAddress.IsLoopback(address) &&
                localAddresses.Contains(server.Endpoint.Address))).ToArray();
        // Multiple names can advertise one endpoint. Conflicting flags cannot seed a session.
        return matches.Length > 0 && matches.All(server => server.Flags == matches[0].Flags) ? matches[0] : null;
    }
}
