using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>One privately prepared Effect and its complete particle stream.</summary>
public sealed record WorldScriptCreatedEffect(int SourceOffset, GoldObjectFxState State,
    WorldObjectNode Node, GoldMessage Publication, uint RandomStateAfter)
{
    public GoldObjectBaseState BaseState => State.BaseState;
    public bool PacketReady => true;
}
