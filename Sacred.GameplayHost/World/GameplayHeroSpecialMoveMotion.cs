namespace Sacred.GameplayHost.World;

/// <summary>Grounded Gold class action starters. Spell selectors are shared separately.
/// Rates identify native calculated inputs; callers must supply them before playback.</summary>
public static class GameplayHeroSpecialMoveMotion
{
    public static GameplayCombatArtAnimation? Select(ushort code, uint creatureType, ushort stance)
    {
        if (creatureType is 2 or 4 or 5 && code is >= 1000 and <= 1002)
            return new(stance is 2 or 3 ? 147U : 146U, 1);
        return creatureType switch
        {
            2 => Gladiator(code, stance),
            3 => code switch
            {
                1000 => new(stance switch { 0 => 219U, 2 => 221U, 4 => 223U, 5 => 224U, 8 => 225U, _ => 220U }, 1),
                1002 => new(stance switch { 0 => 210U, 4 => 214U, 5 => 215U, _ => 211U }, 1, .25f),
                _ => null
            },
            4 => DarkElf(code, stance),
            5 when stance is not (7 or 12) => code switch
            {
                1061 => Attack(stance switch { 0 => 219U, 4 => 223U, 5 => 224U, _ => 220U }, .25f),
                1062 => Attack(stance switch { 0 => 201U, 4 => 205U, 5 => 206U, _ => 202U }, .25f),
                1063 => Attack(stance switch { 0 => 210U, 4 => 214U, 5 => 215U, _ => 211U }, .25f),
                _ => null
            },
            6 or 7 => Vampiress(code, stance),
            8 => GameplayDwarfSpecialMoveMotion.Select(code, stance),
            9 => code switch
            {
                1092 when stance is not (7 or 12) => new(Strong(stance), 1),
                1102 => new(157, 1),
                _ => null
            },
            _ => null
        };
    }

    private static GameplayCombatArtAnimation? Gladiator(ushort code, ushort stance) => code switch
    {
        1010 => new(202, 1),
        1012 => new(146, 1),
        1014 => new(149, 1),
        1015 => new(151, 1),
        1016 => new(150, 1),
        1060 => new(148, 1),
        1057 when stance is not (7 or 12) => Attack(stance switch { 2 => 221U, 3 => 222U, _ => 220U }),
        1058 when stance is not (7 or 12) => Attack(stance switch { 0 => 201U, 2 => 203U, 3 => 204U, 8 => 207U, _ => 202U }),
        1059 when stance is not (7 or 12) => Attack(Strong(stance)),
        _ => null
    };

    private static GameplayCombatArtAnimation? DarkElf(ushort code, ushort stance) => code switch
    {
        1042 => new(148, 1),
        1044 or 1047 or 1048 => new(180, 1),
        1045 => new(146, 1),
        1046 => new(147, 1),
        >= 1049 and <= 1053 => new(109, 1),
        1068 => new(144, 1),
        1065 when stance is not (7 or 12) => Attack(stance switch { 0 => 219U, 2 => 221U, 4 => 223U, 8 => 225U, _ => 220U }),
        1066 when stance is 0 or 1 or 2 or 4 or 8 => Attack(stance switch { 0 => 145U, 2 => 203U, 4 => 205U, 8 => 207U, _ => 202U }),
        1067 when stance is not (7 or 12) => Attack(stance switch { 0 => 210U, 2 => 212U, 8 => 216U, _ => 211U }),
        _ => null
    };

    private static GameplayCombatArtAnimation? Vampiress(ushort code, ushort stance) => code switch
    {
        >= 1000 and <= 1002 => Attack(stance is 2 or 3 ? 147U : 146U),
        1022 => new(144, .7f, .25f),
        1024 => new(149, 1),
        1026 => new(147, 1),
        1076 => new(151, 1),
        1073 when stance is not (7 or 12) => Attack(stance == 8 ? 225U : 221U),
        1075 when stance is not (7 or 12) => Attack(stance switch { 2 => 212U, 8 => 216U, _ => 211U }),
        _ => null
    };

    private static GameplayCombatArtAnimation Attack(uint motion, float blend = .5f) => new(motion, 1, blend)
        { RateSource = GameplayCombatArtRateSource.AttackSpeed };
    private static uint Strong(ushort stance) => stance switch { 0 => 210U, 2 => 212U, 3 => 213U, 8 => 216U, _ => 211U };
}
