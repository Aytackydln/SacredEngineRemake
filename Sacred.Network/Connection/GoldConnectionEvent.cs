using Sacred.Network.Protocol;

namespace Sacred.Network.Connection;

public sealed record GoldFirstContactSent : GoldConnectionEvent;

public abstract record GoldConnectionEvent;
public sealed record GoldTransportConnected(uint ConnectionId) : GoldConnectionEvent;
public sealed record GoldMessagesReceived(IReadOnlyList<GoldMessage> Messages) : GoldConnectionEvent;
public sealed record GoldConnectionClosed(string Reason) : GoldConnectionEvent;
