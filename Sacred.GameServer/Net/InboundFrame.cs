namespace Sacred.GameServer.Net;

/// <summary>A verified TinCat frame handed from an I/O thread to the game loop. <see cref="Payload"/> is rented from ArrayPool and must be returned by the consumer.</summary>
public readonly struct InboundFrame
{
    public const uint Disconnected = uint.MaxValue;
    public readonly IClientLink Connection;
    public readonly uint Sender, Type;
    public readonly byte[] Payload;
    public readonly int Length;

    public InboundFrame(IClientLink connection, uint sender, uint type, byte[] payload, int length)
    { Connection = connection; Sender = sender; Type = type; Payload = payload; Length = length; }

    public ReadOnlySpan<byte> Span => Payload.AsSpan(0, Length);
}
