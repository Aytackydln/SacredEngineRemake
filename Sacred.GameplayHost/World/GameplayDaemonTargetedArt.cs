namespace Sacred.GameplayHost.World;

/// <summary>Gold 0x51BD0A..0x51C5F0: targeted Daemon forms use fixed animation/FX rates.</summary>
public static class GameplayDaemonTargetedArt
{
    private static readonly GameplayCastAttachment[][] Hands = Enumerable.Range(0, 4).Select(index =>
        new GameplayCastAttachment[] { new(17 + index, "Bip01 L Finger3"), new(17 + index, "Bip01 R Finger3") }).ToArray();

    // Gold 0x426610 accepts these authored item categories, including legacy category forms.
    public static bool IsTargetCategory(byte category) => category is 2 or 3 or 32;
    public static bool Handles(uint type, uint code) => type == 9 && code is >= 1097 and <= 1100;
    public static GameplayCombatArtAnimation? Select(uint type, uint code, bool mounted, bool targetEligible) =>
        Handles(type, code) && targetEligible ? new(mounted ? 197U : 160U, 1, .2f) : null;
    public static ReadOnlySpan<GameplayCastAttachment> Attachments(uint code) => code switch
    {
        1097 => Hands[3], 1098 => Hands[1], 1099 => Hands[0], 1100 => Hands[2], _ => []
    };
}
