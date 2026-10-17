namespace Sacred.Core.Pak.Creature;

/// <summary>Fields consumed by Gold 0x4FC490. ActionStage is the client's +0x152 state,
/// separately from the serialized +0x261 simulation state.</summary>
public readonly record struct SacredNpcConversationActor(uint Reference, uint ObjectFlags, uint BehaviorFlags,
    uint ServiceFlags, uint DialogIndex, ushort SimulationState, ushort ActionStage, bool Available);

public enum SacredNpcConversationMotion { None, Talk, AttackReaction }

public sealed record SacredNpcConversationEffect(uint BehaviorFlags, ushort SimulationState,
    bool FaceTarget, bool FaceReceiver, ushort? ReceiverState, SacredNpcConversationMotion Motion)
{
    public const byte TalkMotionSlot = 177;
    public const ushort StartAnimationStage = 101;
    public const ushort PlayingAnimationStage = 102;
}
