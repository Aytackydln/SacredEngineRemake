using System.Numerics;
using Sacred.Core.Pak.Creature;
using Sacred.Network.Gameplay;

namespace Sacred.World.Networking;

public sealed class SacredNetworkNpcConversationState
{
    internal SacredNetworkNpcConversationState(GoldReplicatedObject source, uint behavior, ushort state, long version)
    {
        Source = source; BehaviorFlags = behavior; SimulationState = state; Version = version;
    }
    internal GoldReplicatedObject Source { get; }
    public uint Reference => Source.BaseState.ObjectReference;
    public uint BehaviorFlags { get; }
    public ushort SimulationState { get; internal set; }
    public long Version { get; }
    public ushort ActionStage { get; internal set; }
    public SacredNpcConversationMotion Motion { get; internal set; }
    public Vector2? FacingPosition { get; internal set; }
    public bool NeedsAnimation => ActionStage == SacredNpcConversationEffect.StartAnimationStage;
    public bool NeedsAiResume => ActionStage == 1;
    internal double Elapsed { get; set; }
    internal double Duration { get; set; }
}
