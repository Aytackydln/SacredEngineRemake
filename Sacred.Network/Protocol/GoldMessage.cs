namespace Sacred.Network.Protocol;

/// <summary>A verified application message, separate from TinCat transport framing.</summary>
public sealed record GoldMessage(ushort Opcode, byte Sender, byte Origin, ushort Sequence, byte[] Payload);
