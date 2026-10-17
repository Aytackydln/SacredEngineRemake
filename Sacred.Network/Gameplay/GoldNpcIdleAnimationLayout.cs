namespace Sacred.Network.Gameplay;

/// <summary>Gold NPC_IDLE_ANIMATION, consumed by client 0x7DFA9E and server 0x4C816A.
/// It carries a conversation initiator and target, rather than a model animation index.</summary>
public static class GoldNpcIdleAnimationLayout
{
    public const ushort Opcode = 0x113;
    public const int Size = 8;
    public const int InitiatorOffset = 0;
    public const int TargetOffset = 4;
}
