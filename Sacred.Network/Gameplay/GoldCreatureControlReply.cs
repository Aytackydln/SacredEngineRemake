namespace Sacred.Network.Gameplay;

public sealed record GoldCreatureControlReply(uint ObjectReference, uint ObjectType, long InstanceVersion, byte Owner,
    GoldCreatureUpdateState State, string Reason);
