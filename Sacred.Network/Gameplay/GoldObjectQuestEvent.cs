using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldObjectQuestEvent(GoldMessage Message, uint ObjectReference, uint RawValue) : GoldGameEvent(Message)
{
    public short Value => unchecked((short)RawValue);
    /// <summary>Quest script effect identity; receiving this tag does not start a visual effect.</summary>
    public bool IsEffect => Message.Opcode == GoldObjectQuestLayout.EffectOpcode;
}
