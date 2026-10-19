namespace Sacred.Network.Gameplay;

/// <summary>Gold client 0x805C80, 0x7DE223 and 0x7DDF53. Offsets exclude the application header.</summary>
public static class GoldCombatLayout
{
    public const ushort HealthOpcode = 0x128;
    public const ushort DamageOpcode = 0x17A;
    public const ushort BowOpcode = 0x17B;
    public const ushort BattleOpcode = 0x17C;
    public const int ReferenceOffset = 0;
    public const int HealthSize = 8;
    /// <summary>Absolute signed TP[2], copied by Gold 0x549480; includes regeneration/healing.</summary>
    public const int HitPointsOffset = 4;
    public const int DamageSize = 24;
    /// <summary>The wire word is narrowed to ushort in sDamageParams::dmgPhysical at receive.</summary>
    public const int DamageOffset = 4;
    public const int DamageResourceOffset = 8;
    public const int AttackerOffset = 12;
    public const int ImpactEffectEnabledOffset = 16;
    public const int ImpactEffectDataOffset = 20;
    public const int AttackPrefixSize = 32;
    public const int XOffset = 4;
    public const int YOffset = 8;
    public const int SurfaceOffset = 12;
    public const int SectorOffset = 16;
    /// <summary>Explicit per-model motions[] index, passed to Gold 0x593320.</summary>
    public const int MotionOffset = 20;
    /// <summary>Playback rate before creature +0x2C9 and the model's motion multiplier.</summary>
    public const int SpeedOffset = 24;
    public const int FacingOffset = 28;
    /// <summary>Byte length of the bow reference vector, followed by that vector and action state.</summary>
    public const int BowReferenceBytesOffset = 32;
    public const int BowReferencesOffset = 36;
    public const int MaximumAppliedBowReferenceBytes = 80;
    public const int MaximumAppliedActionStateBytes = 64;
}
