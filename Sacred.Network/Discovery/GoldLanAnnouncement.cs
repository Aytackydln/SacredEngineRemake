using System.Net;

namespace Sacred.Network.Discovery;

/// <summary>The advertised address and TCP port are authoritative; the UDP sender is not the join endpoint.</summary>
public sealed record GoldLanAnnouncement(ushort Version, IPEndPoint Endpoint, uint Flags,
    byte Players, byte MaximumPlayers, string Name);
