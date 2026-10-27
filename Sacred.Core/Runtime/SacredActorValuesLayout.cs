using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Sacred.Core.Binary;

namespace Sacred.Core.Runtime;

[InlineArray(6)] public struct SacredAttributeWords { private ushort _first; }
[InlineArray(8)] public struct SacredActorBytes { private byte _first; }

/// <summary>2004 character value block (sCharValHero), embedded in the character
/// component at +8. Gold saves and full value streams retain the same 238-byte extent;
/// movement, attributes, allocations and learned-skill inputs are verified against Gold CalcResults.</summary>
[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public readonly struct SacredActorValuesLayout
{
    public const int Size = 238;
    [FieldOffset(0)] public readonly uint ItemType;
    [FieldOffset(4)] public readonly uint Experience;
    [FieldOffset(8)] public readonly ushort Strength;
    [FieldOffset(10)] public readonly ushort Endurance;
    [FieldOffset(12)] public readonly ushort Dexterity;
    [FieldOffset(14)] public readonly ushort PhysicalRegeneration;
    [FieldOffset(16)] public readonly ushort MentalRegeneration;
    [FieldOffset(18)] public readonly ushort Charisma;
    /// <summary>First six bytes are allocations in ST, WI, GS, RP, RM, CH order.
    /// Byte six is the calculated attribute-bonus mask below; byte seven remains unresolved.</summary>
    [FieldOffset(20)] public readonly SacredActorBytes AttributeAllocations;
    /// <summary>Native +0x22, Gold CalcResults 0x57B156..0x57B19E: bits 0..5 mark nonzero
    /// calculated bonuses to the six current attribute words at +0x10..+0x1A.
    /// This is an output mask, independent of the six allocated point bytes.</summary>
    [FieldOffset(26)] public readonly byte CalculatedAttributeBonusMask;
    [FieldOffset(28)] public readonly SacredActorBytes SkillIds;
    [FieldOffset(36)] public readonly SacredActorBytes SkillLevels;
    [FieldOffset(44)] public readonly SacredActorBytes SkillBonuses;
    [FieldOffset(52)] public readonly short AttackSpeed;
    [FieldOffset(54)] public readonly short CastSpeed;
    [FieldOffset(56)] public readonly short WalkSpeed;
    /// <summary>Calculated movement percentage, not a world or tile rate. Gold getWalkSpeed 0x585070;
    /// applies to both walk/run template speeds. WalkSpeed retains the original native name.</summary>
    [FieldOffset(56)] public readonly short MovementSpeedPercent;
    [FieldOffset(58)] public readonly ushort RemainingSkillPoints;
    [FieldOffset(60), BinaryUnknown] public readonly ushort SkillLearn;
    [FieldOffset(62)] public readonly uint Gold;
    /// <summary>Runtime base values in ST, WI, GS, RP, RM, CH order, before level growth.</summary>
    [FieldOffset(66)] public readonly SacredAttributeWords BaseAttributes;
    [FieldOffset(78)] public readonly ushort Level;
    /// <summary>Runtime horse movement profile selector; Gold getParam(13) substitutes one for zero
    /// before indexing the Balance.bin movement profile map.</summary>
    [FieldOffset(80)] public readonly byte HorseClass;
    [FieldOffset(81), BinaryUnknown] public readonly byte Reserved;
    [FieldOffset(82)] public readonly float Attack;
    [FieldOffset(86)] public readonly float Defense;
    [FieldOffset(90)] public readonly float MaximumHealth;
    [FieldOffset(94)] public readonly float PhysicalProtection;
    [FieldOffset(98)] public readonly float FireProtection;
    [FieldOffset(102)] public readonly float MagicProtection;
    [FieldOffset(106)] public readonly float PoisonProtection;
    /// <summary>Actor damage stores maxima before minima, unlike the equipment
    /// value block. Gold CalcResults adds equipment +36..42 to +110..122 and
    /// equipment +28..34 to +126..138; its unarmed fallback follows this order.</summary>
    [FieldOffset(110)] public readonly float PhysicalDamageMaximum;
    [FieldOffset(114)] public readonly float FireDamageMaximum;
    [FieldOffset(118)] public readonly float MagicDamageMaximum;
    [FieldOffset(122)] public readonly float PoisonDamageMaximum;
    [FieldOffset(126)] public readonly float PhysicalDamageMinimum;
    [FieldOffset(130)] public readonly float FireDamageMinimum;
    [FieldOffset(134)] public readonly float MagicDamageMinimum;
    [FieldOffset(138)] public readonly float PoisonDamageMinimum;
    // Native accumulator names retained until their application stages are traced.
    [FieldOffset(142), BinaryUnknown] public readonly float BonusPh;
    [FieldOffset(146), BinaryUnknown] public readonly float BonusFe;
    [FieldOffset(150), BinaryUnknown] public readonly float BonusMa;
    [FieldOffset(154), BinaryUnknown] public readonly float BonusGi;
    [FieldOffset(158), BinaryUnknown] public readonly float BonusWPh;
    [FieldOffset(162), BinaryUnknown] public readonly float BonusWFe;
    [FieldOffset(166), BinaryUnknown] public readonly float BonusWMa;
    [FieldOffset(170), BinaryUnknown] public readonly float BonusWGi;
    [FieldOffset(174), BinaryUnknown] public readonly float BonusMPh;
    [FieldOffset(178), BinaryUnknown] public readonly float BonusMFe;
    [FieldOffset(182), BinaryUnknown] public readonly float BonusMMa;
    [FieldOffset(186), BinaryUnknown] public readonly float BonusMGi;
    [FieldOffset(190), BinaryUnknown] public readonly float BonusMWater;
    [FieldOffset(194), BinaryUnknown] public readonly float BonusMAir;
    [FieldOffset(198), BinaryUnknown] public readonly float BonusMEarth;
    [FieldOffset(202), BinaryUnknown] public readonly float BonusMLost;
    [FieldOffset(206), BinaryUnknown] public readonly float BonusRPh;
    [FieldOffset(210), BinaryUnknown] public readonly float BonusRFe;
    [FieldOffset(214), BinaryUnknown] public readonly float BonusRMa;
    [FieldOffset(218), BinaryUnknown] public readonly float BonusRGi;
    [FieldOffset(222), BinaryUnknown] public readonly float BonusAttack;
    [FieldOffset(226), BinaryUnknown] public readonly float BonusDefense;
    [FieldOffset(230), BinaryUnknown] public readonly float BonusSpellRegeneration;
    [FieldOffset(234), BinaryUnknown] public readonly float BonusCombatArtRegeneration;
}
