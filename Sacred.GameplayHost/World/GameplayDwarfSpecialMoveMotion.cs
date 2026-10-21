namespace Sacred.GameplayHost.World;

/// <summary>Gold 0x5185D5 class starters. The native checks archive presence for weapon motions.</summary>
internal static class GameplayDwarfSpecialMoveMotion
{
    public static GameplayCombatArtAnimation? Select(ushort code, ushort stance) => code switch
    {
        1078 when stance is not (7 or 12) => new(stance switch { 0 => 219U, 2 => 221U, 3 => 222U, 13 or 14 => 248U, _ => 220U }, 1)
            { FallbackMotion = 220 },
        1079 when stance is not (7 or 12) => new(stance switch { 0 => 201U, 2 => 203U, 3 => 204U, 8 => 207U, 13 => 247U, 14 => 148U, _ => 202U }, 1)
            { FallbackMotion = 202 },
        1080 when stance is not (7 or 12) => new(stance switch { 0 => 210U, 2 => 212U, 3 => 213U, 8 => 216U, 13 or 14 => 248U, _ => 211U }, 1)
            { FallbackMotion = 211 },
        1082 => new(157, 1),
        1083 => new(201, 1),
        1084 => new(144, 1),
        1085 or 1086 or 1087 => new(code switch { 1085 => 147U, 1086 => 145U, _ => 146U }, 1)
            { RateSource = GameplayCombatArtRateSource.CastSpeed },
        1089 => new(154, 1),
        _ => null
    };
}
