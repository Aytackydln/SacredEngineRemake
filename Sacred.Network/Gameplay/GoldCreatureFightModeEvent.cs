using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public sealed record GoldCreatureFightModeEvent(GoldMessage Message, uint ObjectReference, uint Flags) : GoldGameEvent(Message)
{
    public bool Enabled => (Flags & GoldCreatureStateLayout.FightModeFlag) != 0;
}
