namespace Sacred.Network.Gameplay;

/// <summary>NW_CREATURE_SLEEP: Gold client 0x7DDDFB and server 0x4C5904.</summary>
public static class GoldCreatureSleepLayout
{
    public const ushort Opcode = 0x126;
    public const int Size = 8;
    public const int ReferenceOffset = 0;
    /// <summary>Remaining AI tick delay: elapsed (+0x208) = interval (+0x204) - this float.
    /// Owner Think wakes only after elapsed exceeds the interval, subject to its clock normalization and guards.</summary>
    public const int ClockOffsetOffset = 4;
    /// <summary>The receiver explicitly requests this Models.tmp motion slot, at rate one.</summary>
    public const uint Motion = 175;
}
