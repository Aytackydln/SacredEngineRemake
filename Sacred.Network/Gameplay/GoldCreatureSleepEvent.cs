using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldCreatureSleepEvent(GoldMessage Message, uint ObjectReference, uint ClockOffsetBits)
    : GoldCombatEvent(Message, ObjectReference)
{
    public float ClockOffset => BitConverter.UInt32BitsToSingle(ClockOffsetBits);
}
