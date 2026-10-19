using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public abstract record GoldCombatEvent(GoldMessage Message, uint ObjectReference) : GoldGameEvent(Message);

public sealed record GoldCreatureHealthEvent(GoldMessage Message, uint ObjectReference, int HitPoints)
    : GoldCombatEvent(Message, ObjectReference);

public sealed record GoldCreatureDamageEvent(GoldMessage Message, uint ObjectReference, uint DamageWord,
    uint Resource, uint AttackerReference, uint ImpactEffectEnabled, uint ImpactEffectData)
    : GoldCombatEvent(Message, ObjectReference)
{
    public ushort Damage => unchecked((ushort)DamageWord);
}

public sealed record GoldCreatureAttackEvent(GoldMessage Message, uint ObjectReference, GoldObjectPosition Position,
    uint Motion, uint SpeedBits, uint FacingBits, byte[] BowReferenceData, byte[] ActionState)
    : GoldCombatEvent(Message, ObjectReference)
{
    public bool IsBow => Message.Opcode == GoldCombatLayout.BowOpcode;
    public float Speed => BitConverter.UInt32BitsToSingle(SpeedBits);
}
