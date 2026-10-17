namespace Sacred.Core.Pak.Creature;

/// <summary>Gold 0x4FC490 client conversation decisions. Random results are native CRT
/// nonnegative 15-bit values; callers supply the stream and original template/name predicates.</summary>
public static class SacredNpcConversationRules
{
    public static SacredNpcConversationEffect Apply(SacredNpcConversationActor source, SacredNpcConversationActor? target,
        bool excludedType, bool reactionType, uint distance, Func<int> random, Func<uint, uint?> dialogIcon)
    {
        var effect = new SacredNpcConversationEffect(source.BehaviorFlags & ~4u, 0, false, false, null, SacredNpcConversationMotion.None);
        if (excludedType || random() % 2 == 0 || target is not { } receiver) return effect;
        effect = effect with { FaceTarget = true };
        // An aliased target observes the initiator's action-stage reset before this test.
        if (receiver.Reference != source.Reference && receiver.ActionStage >= 100) return effect;
        if (unchecked((int)source.DialogIndex) > 0 &&
            (source.DialogIndex >= 160000 ? 4 : dialogIcon(source.DialogIndex) ?? 0) < 51) return effect;
        if ((receiver.ServiceFlags & 0x7000) != 0) return effect;
        var faceReceiver = receiver.Available && (receiver.ServiceFlags & 0x490) == 0 && (receiver.ObjectFlags & 2) == 0;
        ushort state = 2;
        if (random() % 2 != 0) state = (ushort)(random() % 7 + 30);
        effect = effect with { SimulationState = state, ReceiverState = 3, FaceReceiver = faceReceiver, Motion = SacredNpcConversationMotion.Talk };
        if (source.Reference > 16 && receiver.Reference > 16 && reactionType && random() % 11 == 0 && distance is > 100 and < 140)
            effect = effect with { SimulationState = 35, ReceiverState = 30, Motion = SacredNpcConversationMotion.AttackReaction };
        return effect;
    }
}
