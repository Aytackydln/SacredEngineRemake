namespace Sacred.GameplayHost.World;

/// <summary>Gold EiVampiress 0x5132C2 waits for vampire form before starting
/// arts 1030..1037. Their on-foot starters use fixed rates; weapon arts use attack speed.</summary>
public static class GameplayVampiressArtMotion
{
    public static GameplayCombatArtAnimation? Select(ushort code, uint flags, bool mounted)
    {
        if ((flags & 0x2000) == 0 || mounted) return null;
        return code switch
        {
            1030 => new(144, 1),
            1031 => new(154, 1),
            1033 => new(148, 1),
            1035 or 1036 or 1037 => new(145, 1),
            1073 => new(219, 1) { RateSource = GameplayCombatArtRateSource.AttackSpeed },
            1074 => new(201, 1) { RateSource = GameplayCombatArtRateSource.AttackSpeed },
            1075 => new(210, 1) { RateSource = GameplayCombatArtRateSource.AttackSpeed },
            // 1032 first validates its jump path; 1034 changes an effect without a starter.
            _ => null
        };
    }
}
