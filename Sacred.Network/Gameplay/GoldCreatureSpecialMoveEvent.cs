using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless special-move action and reserved objects; animation selection is a gameplay rule.</summary>
public sealed record GoldCreatureSpecialMoveEvent(GoldMessage Message, uint ObjectReference,
    byte[] ActionState, byte[] ReferenceData) : GoldCombatEvent(Message, ObjectReference)
{
    public ushort Code => ReadShort(GoldSpecialMoveLayout.CodeOffset);
    public uint TargetReference => ReadWord(GoldSpecialMoveLayout.TargetReferenceOffset);
    public int TargetX => unchecked((int)ReadWord(GoldSpecialMoveLayout.TargetXOffset));
    public int TargetY => unchecked((int)ReadWord(GoldSpecialMoveLayout.TargetYOffset));
    private uint ReadWord(int offset) => ActionState.Length >= offset + 4
        ? BinaryPrimitives.ReadUInt32LittleEndian(ActionState.AsSpan(offset)) : 0;
    private ushort ReadShort(int offset) => ActionState.Length >= offset + 2
        ? BinaryPrimitives.ReadUInt16LittleEndian(ActionState.AsSpan(offset)) : (ushort)0;
}
