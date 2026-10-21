using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Lossless spell action and reservations. Native receive clears its 64-byte bank before copying.</summary>
public sealed record GoldCreatureCastEvent(GoldMessage Message, uint ObjectReference,
    byte[] ActionState, byte[] ReferenceData) : GoldCombatEvent(Message, ObjectReference)
{
    public uint Code => Word(GoldCastLayout.CodeOffset);
    public uint TargetReference => Word(GoldCastLayout.TargetReferenceOffset);
    public int TargetX => unchecked((int)Word(GoldCastLayout.TargetXOffset));
    public int TargetY => unchecked((int)Word(GoldCastLayout.TargetYOffset));
    public uint TargetSector => Word(GoldCastLayout.SectorOffset);
    public uint TargetSurface => Word(GoldCastLayout.SurfaceOffset);
    public byte OriginPlayer => unchecked((byte)Message.Origin);
    private uint Word(int offset) => ActionState.Length >= offset + 4
        ? BinaryPrimitives.ReadUInt32LittleEndian(ActionState.AsSpan(offset)) : 0;
}
