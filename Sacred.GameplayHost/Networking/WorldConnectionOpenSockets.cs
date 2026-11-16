namespace Sacred.GameplayHost.Networking;

/// <summary>Native socket services. Bind receives the established current socket
/// and unnormalized caller port; its provider owns native address construction.</summary>
/// <remarks>Startup owns WSAStartup(0x0202); Create owns socket(2,2,0).
/// ReuseAddress owns setsockopt(SOL_SOCKET,4,int32(1)); Bind owns the original
/// htons/htonl and 16-byte IPv4 sockaddr. Platform statuses stay raw.</remarks>
public sealed record WorldConnectionOpenSockets(Func<int> Startup, Func<uint> Create,
    Func<uint, int> SetReuseAddress, Func<uint, uint, int> Bind, Func<uint> LastError);
