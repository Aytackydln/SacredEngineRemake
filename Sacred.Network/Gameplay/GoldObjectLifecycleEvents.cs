using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>The native removal lookup clears the reference's high bit. Retain the original wire word.</summary>
public sealed record GoldObjectRemovalEvent(GoldMessage Message, uint ReferenceWord) : GoldGameEvent(Message)
{
    public uint ObjectReference => ReferenceWord & GoldObjectLifecycleLayout.RemovalReferenceMask;
    public bool Immediate => Message.Opcode == GoldObjectLifecycleLayout.ImmediateRemoveOpcode;
}

/// <summary>Client 0x7DD69E writes the low byte of the second word into object +0x39.</summary>
public sealed record GoldObjectOwnerEvent(GoldMessage Message, uint ObjectReference, uint OwnerWord) : GoldGameEvent(Message)
{
    public byte Owner => unchecked((byte)OwnerWord);
}
