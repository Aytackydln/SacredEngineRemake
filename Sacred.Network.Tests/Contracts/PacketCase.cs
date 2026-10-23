using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.Network.Tests.Contracts;

internal enum PayloadRule { Exact, RetainedSuffix, Structured }

internal sealed record PacketCase(string Name, ushort Opcode, Type EventType, Func<byte[]> Payload,
    PayloadRule Rule = PayloadRule.Exact, Action<GoldGameEvent>? Verify = null, byte Sender = 0,
    bool OwnsMessage = false, int[]? ValidShorterLengths = null)
{
    public string Id => $"0x{Opcode:X4} {Name}";
    public GoldMessage Message() => new(Opcode, Sender, 1, ushort.MaxValue, Payload());
}
