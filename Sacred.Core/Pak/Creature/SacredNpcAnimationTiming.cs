namespace Sacred.Core.Pak.Creature;

/// <summary>Gold 0x544BCC, 0x593320 and 0x413610: action speed and fade timing.
/// AttackSpeed is the calculated signed value returned by 0x584FE0, before its 220 cap.</summary>
public readonly record struct SacredNpcAnimationTiming(float PlaybackSpeed, float BlendSeconds, float ActorFactor)
{
    public static SacredNpcAnimationTiming Conversation(short attackSpeed, float actorFactor, float motionFactor = 1)
    {
        var rate = Math.Min(220, (int)attackSpeed) * actorFactor * .01f * actorFactor;
        if (!float.IsFinite(rate) || rate <= 0 || !float.IsFinite(motionFactor) || motionFactor <= 0)
            throw new ArgumentOutOfRangeException(nameof(attackSpeed), "Positive finite native playback speed required.");
        return new(rate * motionFactor, Math.Min(.2f, .25f / rate), actorFactor);
    }
    public float ReturnBlendSeconds => Math.Min(.2f, .05f / ActorFactor);
}
