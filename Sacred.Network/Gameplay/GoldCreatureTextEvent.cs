using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldCreatureTextEvent(GoldMessage Message, uint ObjectReference, uint TextId) : GoldCombatEvent(Message, ObjectReference)
{
    /// <summary>The native receiver copies XY into a global UI event. Later movement/removal does not move or cancel it.</summary>
    public GoldObjectPosition? ReceiptPosition { get; init; }
}
