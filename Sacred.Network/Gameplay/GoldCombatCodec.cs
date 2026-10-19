using System.Buffers.Binary;
using Sacred.Network.Protocol;

namespace Sacred.Network.Gameplay;

public static class GoldCombatCodec
{
    public static GoldCombatEvent Decode(GoldMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var bytes = message.Payload.AsSpan();
        var size = message.Opcode switch
        {
            GoldCombatLayout.HealthOpcode => GoldCombatLayout.HealthSize,
            GoldCombatLayout.DamageOpcode => GoldCombatLayout.DamageSize,
            GoldCombatLayout.BattleOpcode => GoldCombatLayout.AttackPrefixSize,
            GoldCombatLayout.BowOpcode => GoldCombatLayout.BowReferencesOffset,
            _ => throw new ArgumentOutOfRangeException(nameof(message))
        };
        if (bytes.Length < size || message.Opcode != GoldCombatLayout.BowOpcode && bytes.Length != size)
            throw new InvalidDataException($"Invalid Gold combat payload size for 0x{message.Opcode:X}.");
        var owned = message with { Payload = bytes.ToArray() };
        uint Word(int offset) => BinaryPrimitives.ReadUInt32LittleEndian(owned.Payload.AsSpan(offset));
        var reference = Word(GoldCombatLayout.ReferenceOffset);
        if (message.Opcode == GoldCombatLayout.HealthOpcode)
            return new GoldCreatureHealthEvent(owned, reference, unchecked((int)Word(GoldCombatLayout.HitPointsOffset)));
        if (message.Opcode == GoldCombatLayout.DamageOpcode)
            return new GoldCreatureDamageEvent(owned, reference, Word(GoldCombatLayout.DamageOffset),
                Word(GoldCombatLayout.DamageResourceOffset), Word(GoldCombatLayout.AttackerOffset),
                Word(GoldCombatLayout.ImpactEffectEnabledOffset), Word(GoldCombatLayout.ImpactEffectDataOffset));
        var count = message.Opcode == GoldCombatLayout.BowOpcode ? Word(GoldCombatLayout.BowReferenceBytesOffset) : 0;
        if (count > bytes.Length - size || (count & 3) != 0)
            throw new InvalidDataException("Gold bow reference data must contain bounded four-byte references.");
        return new GoldCreatureAttackEvent(owned, reference,
            new(unchecked((int)Word(GoldCombatLayout.XOffset)), unchecked((int)Word(GoldCombatLayout.YOffset)),
                Word(GoldCombatLayout.SurfaceOffset), Word(GoldCombatLayout.SectorOffset)),
            Word(GoldCombatLayout.MotionOffset), Word(GoldCombatLayout.SpeedOffset), Word(GoldCombatLayout.FacingOffset),
            bytes.Slice(size, (int)count).ToArray(), bytes[(size + (int)count)..].ToArray());
    }
}
