using Sacred.Core.Pax.Hero.Objects;

namespace Sacred.Network.Gameplay;

/// <summary>Gold 0x54BC70 fixed creature prefix, ending before its variable subtype callback at 0x54BE73.</summary>
public static class GoldCreatureStateLayout
{
    public const int PrefixSize = 445;
    public const int MarkerOffset = GoldPhysicalStateLayout.Size;
    public const uint Marker = 0xC0DEF00D;
    public const int EquipmentOffset = 125;
    public const int EquipmentCount = 19;
    public const int LeftHandEquipmentIndex = SacredSavedCreatureLayout.LeftHandEquipmentIndex;
    public const int RightHandEquipmentIndex = SacredSavedCreatureLayout.RightHandEquipmentIndex;
    public const int Word1F0Offset = 201;
    public const int Word1F4Offset = 205;
    public const int Word1F8Offset = 209;
    public const int LocomotionActionOffset = Word1F8Offset;
    public const int Byte1FCOffset = 213;
    public const int Byte1FDOffset = 214;
    public const int Word1FEOffset = 215;
    public const int Word200Offset = 217;
    public const uint SleepingFlag = SacredSavedCreatureLayout.SleepingFlag;
    public const uint FightModeFlag = SacredSavedCreatureLayout.FightModeFlag;
    public const uint SleepWakeSuppressedFlag = SacredSavedCreatureLayout.SleepWakeSuppressedFlag;
    /// <summary>Gold EiBow (0x4DCB80) uses separate projectile factories for native +0x200 bits
    /// 0x200000 (BFG) and 0x400000, independently of the action-bank art byte.</summary>
    public const uint AlternateBowProjectileMask = 0x00600000;
    public const int Word245Offset = 221;
    public const int Word249Offset = 225;
    public const int Word24DOffset = 229;
    public const int Word251Offset = 233;
    public const int Word255Offset = 237;
    public const int Word259Offset = 241;
    public const int Word25FOffset = 245;
    public const int Word261Offset = 247;
    public const int Byte2B6Offset = 249;
    public const int Word2B7Offset = 250;
    public const int Block2B9Offset = 252;
    public const int Block2B9Size = 16;
    public const int Word2C9Offset = 268;
    /// <summary>Native +0x2C9 float factor used by action-animation playback and walk/run path speed (0x4F761B).</summary>
    public const int AnimationSpeedMultiplierOffset = Word2C9Offset;
    public const int Block2EDOffset = 272;
    public const int Block2EDSize = 173;
    // Path mode reuses this action bank; native +0x36D/+0x371 are index/count.
    public const int MovementPathIndexOffset = Block2EDOffset + 128;
    public const int MovementPathCountOffset = MovementPathIndexOffset + 4;
    public const int MovementSpeedOffset = Block2EDOffset + GoldMovementPathLayout.SpeedOffset;
    public const int MovementDistanceFractionOffset = Block2EDOffset + GoldMovementPathLayout.DistanceFractionOffset;
    public const int MovementRemainderXOffset = Block2EDOffset + GoldMovementPathLayout.RemainderXOffset;
    public const int MovementRemainderYOffset = Block2EDOffset + GoldMovementPathLayout.RemainderYOffset;
    public const int MovementLastClockOffset = Block2EDOffset + GoldMovementPathLayout.LastClockOffset;
    public const int MovementElapsedOffset = Block2EDOffset + GoldMovementPathLayout.ElapsedOffset;
    public const int MovementTargetXOffset = Block2EDOffset + 164;
    public const int MovementTargetYOffset = Block2EDOffset + 168;
}
