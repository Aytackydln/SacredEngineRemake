namespace Sacred.Core.Pax.Hero.Objects;

/// <summary>Current-format file method 0x54B3D0, independent of the packet method 0x54BC70.</summary>
public static class SacredSavedCreatureLayout
{
    public const int MarkerOffset = 247;
    public const int BlockFCOffset = 251;
    public const int Block150Offset = 335;
    /// <summary>Packed native +0x174 control copied into Gold movement message prefixes.</summary>
    public const int MovementControlOffset = Block150Offset + 0x24;
    public const int EquipmentOffset = 419;
    public const int EquipmentCount = 19;
    // Gold 0x401AE0 binds native slot 12 to L Hand/Forearm and 13 to R Hand/Forearm.
    public const int LeftHandEquipmentIndex = 12;
    public const int RightHandEquipmentIndex = 13;
    public const int LeftHandEquipmentOffset = EquipmentOffset + LeftHandEquipmentIndex * 4;
    public const int RightHandEquipmentOffset = EquipmentOffset + RightHandEquipmentIndex * 4;
    public const int Block1F0Offset = 495;
    /// <summary>Native +0x1F8 locomotion selector: 1 walking, 2 running (Gold Shift-key producer 0x4F5193).</summary>
    public const int LocomotionActionOffset = Block1F0Offset + 8;
    public const int Word200Offset = 511;
    /// <summary>Native +0x2C9 float, multiplied into action-animation speed at Gold 0x544C0B.
    /// Also multiplies movement percentage before base walk/run rates (Gold 0x4F761B).
    /// Shared file prefix is copied by GoldSavedCreatureStateImporter.</summary>
    public const int AnimationSpeedMultiplierOffset = Block1F0Offset + 0xD9;
    public const int Block2EDOffset = 748;
    public const int Block2EDSize = 173;
    // Native +0x2ED is an action bank. During path movement its first 128 bytes
    // hold signed XY pairs; +0x36D/+0x371 hold the current index and count.
    // Other actions reuse this bank; these names do not classify every save as a path.
    public const int MovementPathOffset = Block2EDOffset;
    public const int MovementPathPointSize = 8;
    public const int MovementPathStorageSize = 128;
    public const int MovementPathIndexOffset = Block2EDOffset + MovementPathStorageSize;
    public const int MovementPathCountOffset = MovementPathIndexOffset + 4;
    /// <summary>Gold 0x4F75F0: integer native units per second after stat, actor and GameSpeed factors.</summary>
    public const int MovementSpeedOffset = Block2EDOffset + 136;
    /// <summary>Gold 0x5953EB: Q16 fractional distance carried between movement ticks.</summary>
    public const int MovementDistanceFractionOffset = Block2EDOffset + 140;
    /// <summary>Gold 0x59561E: signed Q10 X/Y remainders carried between ticks.</summary>
    public const int MovementRemainderXOffset = Block2EDOffset + 144;
    public const int MovementRemainderYOffset = Block2EDOffset + 148;
    /// <summary>Native +0x385 clock stored by the movement producer.</summary>
    public const int MovementLastClockOffset = Block2EDOffset + 152;
    /// <summary>Native +0x389 float seconds accumulated by path playback.</summary>
    public const int MovementElapsedOffset = Block2EDOffset + 156;
    // Native movement producer 0x4F4770 stores its desired X/Y at +0x391/+0x395.
    public const int MovementTargetXOffset = Block2EDOffset + 164;
    public const int MovementTargetYOffset = Block2EDOffset + 168;
    public const int Word570Offset = 921;
    /// <summary>Gold 0x43C0D0: linked horse on a rider, linked rider on a horse (native +0x570).</summary>
    public const int MountPartnerReferenceOffset = Word570Offset;
    public const int Word574Offset = 925;
    public const int Word578Offset = 927;
    public const int ReservedOffset = 931;
    public const int ValuesOffset = 945;
    public const uint HeroAttachmentFlag = 0x40;
    public const int HeroAttachmentSize = 444;
    public const int Record9Size = 9;
    public const int Record68Size = 68;
}
