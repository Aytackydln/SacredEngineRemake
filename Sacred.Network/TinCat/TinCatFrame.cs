namespace Sacred.Network.TinCat;

public sealed record TinCatFrame(uint Sender, uint Recipient, uint Type, uint Reserved, byte[] Payload);
