using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

/// <summary>Original flight operands. Translation/rotation retain both native union interpretations.</summary>
public sealed record GoldObjectFlightEvent(GoldMessage Message, uint ObjectReference,
    GoldObjectPosition Position, GoldPhysicalVector Translation, GoldPhysicalVector Rotation,
    float InitialHeight) : GoldGameEvent(Message)
{
    public bool Forced => Message.Opcode == GoldObjectFlightLayout.ForcedOpcode;
    public bool IsItemArc => Rotation.X == GoldObjectFlightLayout.ItemArcMode;
}
