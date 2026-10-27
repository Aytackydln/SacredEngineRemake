using System.Buffers.Binary;
using Sacred.Core.GameBin.Balance;

namespace Sacred.Core.Runtime;

/// <summary>Gold 0x566070 scalar passive results, with an explicitly empty
/// learned-combat-art list and no description request. School skills traverse
/// that empty list. Ranked skills run in the eight stored slots, without the
/// separate lookup helper's 255 clamp. Returns TP[4] after Constitution.</summary>
public static class SacredCreaturePassiveSkills
{
    public static uint Apply(byte[] values, uint regeneration, int learnedCombatArtCount,
        SacredCreatureSkillContext actor, SacredCreatureSkillBalanceLayout tuning)
    {
        if (values.Length != SacredActorValuesLayout.Size) throw new ArgumentException("Expected 238-byte actor values.", nameof(values));
        if (learnedCombatArtCount != 0) throw new NotSupportedException("Passive skill application to learned combat arts requires their native entry effects.");
        for (var slot = 0; slot < 8; slot++)
        {
            var rank = values[36 + slot] + values[44 + slot];
            if (rank == 0) continue;
            switch (values[28 + slot])
            {
                case 2:
                    for (var i = 0; i < 4; i++) AddPercent(158 + i * 4, tuning.WeaponLoreDamage);
                    break;
                case 3:
                    if (actor.Skill3Weapon && !actor.Skill6Weapon) Weapon(tuning.Skill3Attack, tuning.Skill3Speed);
                    break;
                case 4:
                    if (actor.Skill4Weapon && !actor.Skill6Weapon) Weapon(tuning.Skill4Attack, tuning.Skill4Speed);
                    break;
                case 5:
                    if (actor.Skill5Weapon && !actor.Skill6Weapon) Weapon(tuning.Skill5Attack, tuning.Skill5Speed);
                    break;
                case 6:
                    if (actor.Skill6Weapon || actor.MainUsage == 0 && actor.OffhandPresent) Weapon(tuning.Skill6Attack, tuning.Skill6Speed);
                    break;
                case 7:
                    if (actor.Skill7Weapon) Weapon(tuning.Skill7Attack, tuning.Skill7Speed);
                    break;
                case 8:
                    AddPercent(222, tuning.Skill8Attack);
                    AddPercent(226, tuning.Skill8Defense);
                    break;
                case 9:
                    AddPercent(226, actor.Skill9AlternateCurve ? tuning.Skill9AlternateDefense : tuning.Skill9Defense);
                    break;
                case 10:
                    var signed = unchecked((int)regeneration);
                    regeneration = unchecked((uint)(long)(signed + signed * (double)Curve(tuning.ConstitutionRegeneration) * 0.01f));
                    break;
                case 11:
                    AddPercent(206, tuning.ArmorLorePhysical);
                    AddPercent(210, tuning.ArmorLoreFire);
                    AddPercent(214, tuning.ArmorLoreMagic);
                    AddPercent(218, tuning.ArmorLorePoison);
                    AddSpeed(56, tuning.ArmorLoreMovement);
                    break;
                case 12:
                    AddPercent(230, tuning.Skill12Regeneration);
                    break;
                case 13:
                    if (actor.MainUsage == 10 || actor.OffhandWeaponUsage10) Weapon(tuning.Skill13Attack, tuning.Skill13Speed);
                    break;
                case 14:
                    for (var i = 0; i < 4; i++) AddPercent(174 + i * 4, tuning.SpellDamage);
                    break;
                case 24:
                    if (actor.MainUsage == 0) Weapon(tuning.UnarmedAttack, tuning.UnarmedSpeed);
                    break;
            }

            float Curve(SacredSkillCurveLayout curve)
            {
                var amount = ((double)curve.Limit - curve.Base) * (1d - 1d / ((rank - 1d) / curve.Divisor + 1d));
                return (float)(amount + amount + curve.Base);
            }
            void AddPercent(int at, SacredSkillCurveLayout curve)
                => BinaryPrimitives.WriteSingleLittleEndian(values.AsSpan(at), (float)(Curve(curve) * (double)0.01f + BinaryPrimitives.ReadSingleLittleEndian(values.AsSpan(at))));
            void AddSpeed(int at, SacredSkillCurveLayout curve)
                => BinaryPrimitives.WriteInt16LittleEndian(values.AsSpan(at), unchecked((short)(int)(BinaryPrimitives.ReadInt16LittleEndian(values.AsSpan(at)) + (double)Curve(curve))));
            void Weapon(SacredSkillCurveLayout attack, SacredSkillCurveLayout speed)
            {
                AddPercent(222, attack);
                AddSpeed(52, speed);
            }
        }
        return regeneration;
    }
}
