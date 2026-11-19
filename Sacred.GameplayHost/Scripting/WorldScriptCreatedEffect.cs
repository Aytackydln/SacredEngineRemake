using Sacred.Network.Gameplay;
using Sacred.Network.Protocol;

namespace Sacred.GameplayHost.Scripting;

/// <summary>One privately prepared Effect and its complete particle stream.</summary>
public sealed record WorldScriptCreatedEffect(int SourceOffset, GoldObjectFxState State,
    WorldObjectNode Node, GoldMessage Publication, uint RandomStateAfter)
{
    public GoldObjectBaseState? RuntimeState { get; init; }
    public uint NextOnTile { get; init; }
    public GoldObjectBaseState BaseState => RuntimeState ?? State.BaseState;
    public bool PacketReady => true;
}
