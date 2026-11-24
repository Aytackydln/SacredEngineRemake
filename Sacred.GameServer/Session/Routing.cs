namespace Sacred.GameServer.Session;

/// <summary>
/// Recipient selection of GameServer's forwarder <c>FUN_004d3ea0(except, data, size, opcode, origin, mode, objectRef)</c>.
/// </summary>
public enum RouteMode : byte
{
    None = 0,
    /// <summary>1: every other connected player.</summary>
    Connected = 1,
    /// <summary>2: every other player that has entered the world (+0x75 set by 0x17/0x18).</summary>
    InGame = 2,
    /// <summary>3: in game and the referenced object's sector is visible to the player.</summary>
    SectorVisible = 3,
    /// <summary>5: same party/team as the origin.</summary>
    Party = 5,
    /// <summary>6: players in the referenced object's visibility mask (+0x3A).</summary>
    ObjectVisible = 6,
    /// <summary>7: in game and in the object's sector.</summary>
    SameSector = 7,
    /// <summary>8: sector visible and in the object's visibility mask.</summary>
    SectorAndObject = 8,
}

/// <summary>
/// Pure forwards of the original dispatcher <c>FUN_004c3f20</c> (switch on opcode-3, index table 0x4CC9C4, jump table 0x4CC6E0):
/// the cases whose only network effect is re-sending the incoming message through <c>FUN_004d3ea0</c>. Extracted from the call
/// sites' pushed arguments (see README "Routing"). Opcodes that are not listed here and not handled explicitly are dropped by the
/// original too (its default case only logs).
/// </summary>
public static class Routing
{
    public const int OpcodeLimit = 0x200;
    private static readonly RouteMode[] Modes = new RouteMode[OpcodeLimit];
    private static readonly bool[] IncludesSender = new bool[OpcodeLimit];

    static Routing()
    {
        Set(RouteMode.Connected, 0x151, 0x138, 0x13B);
        Set(RouteMode.InGame, 0x15E, 0x160, 0x185, 0x186, 0x1B0, 0x1B6, 0x1B7, 0x1BA, 0x1BB, 0x1BF, 0x1C1, 0x1C6, 0x1C8, 0x1CC,
            0x1CF, 0x1D4, 0x1D5, 0x18E, 0x18F, 0x190, 0x191, 0x192, 0x193, 0x194, 0x195,
            0x125, 0x149, 0x14A, 0x14D, 0x14E, 0x181, 0x182, 0x184, 0x189, 0x196);
        Set(RouteMode.SectorVisible, 0x111, 0x112, 0x18C, 0x18D, 0x199, 0x19B);
        Set(RouteMode.Party, 0x145);
        Set(RouteMode.ObjectVisible, 0x11A, 0x11B, 0x14B, 0x169, 0x16B, 0x16C, 0x16D, 0x16E, 0x16F, 0x177, 0x1A6, 0x1A9);
        Set(RouteMode.SectorAndObject, 0x113, 0x11F, 0x141, 0x161, 0x163, 0x183);
        Set(RouteMode.SectorAndObject, 0x11E);
        IncludesSender[0x11E] = true; // pushed except = 0x11 (nobody excluded)

        // Cases that first update the server's own world (object values, creature state, quests, ... - not modelled yet) and
        // then forward the message unchanged. Same extraction; the update call of each case is named in the README.
        Set(RouteMode.InGame, 0x10D, 0x15D, 0x187, 0x188, 0x1AE, 0x1AF, 0x1B8, 0x1B9, 0x1BC, 0x1BD, 0x1BE, 0x1C0, 0x1C2, 0x1C3,
            0x1C4, 0x1C5, 0x1C7, 0x1C9, 0x1CB, 0x1CD, 0x1CE, 0x1D0, 0x1D1, 0x1D2, 0x1D3);
        Set(RouteMode.SectorVisible, 0x12D, 0x15C, 0x1A4, 0x1A5);
        Set(RouteMode.Party, 0x128);
        Set(RouteMode.ObjectVisible, 0x114, 0x127, 0x12E, 0x173, 0x175, 0x17A);
        Set(RouteMode.SectorAndObject, 0x119, 0x11C, 0x11D, 0x120, 0x121, 0x126, 0x12F, 0x13C, 0x140, 0x146, 0x147, 0x162, 0x164,
            0x165, 0x170, 0x174, 0x178, 0x179, 0x17B, 0x17C);
        IncludesSender[0x11D] = true;

        // Consumed by the server's own state (inventories and NW_ITEM cheat protection 0x12x/0x13E/0x144, references, free-slot
        // checks, ...): no network effect in the original either, so they are dropped without the "unhandled" log.
        foreach (var opcode in (ReadOnlySpan<ushort>)[0x10E, 0x10F, 0x122, 0x123, 0x129, 0x12A, 0x12B, 0x12C, 0x130, 0x131, 0x13E,
            0x144, 0x14F, 0x155, 0x172, 0x176, 0x17F, 0x18A, 0x197, 0x19C, 0x19D])
            Consumed[opcode] = true;
        // 0x14F (9940 bytes, sent on entering and on leaving): the player's statistics block; case 0x4C6030 copies it into the
        // cStatsManager slot of the sender (FUN_00477500 / FUN_00477490, 0x26D4 bytes) and does nothing else.
        // 0x17F NW_FIGHT_ENEMY_SPOTTED (case 0x4C61C7 -> FUN_004CE530, pairs [creature ref][value]): sets/clears the creature's
        // 0x8000 fight flag in the server's own object and sends 0x119 (ref, flags) to the in-game players only when it changed,
        // then hands creatures over to the spotting player (FUN_004D6950). Needs the real object state, so it is consumed here.
        // Commands the original's own menu/lobby thread posts into its queue (lobby connect/logout 0x10A..0x10C, 0x109, release
        // refs 0x152, savegame start positions 0x15A, host hand-over 0x15B). A client must not be able to trigger them, so they
        // are dropped. 0x153/0x154 are client requests (Session/GameServerHost.References.cs).
        foreach (var opcode in (ReadOnlySpan<ushort>)[0x109, 0x10A, 0x10B, 0x10C, 0x152, 0x15A, 0x15B])
            Internal[opcode] = true;
    }

    private static readonly bool[] Consumed = new bool[OpcodeLimit];
    private static readonly bool[] Internal = new bool[OpcodeLimit];

    /// <summary>Messages the original only applies to its own state.</summary>
    public static bool IsConsumed(ushort opcode) => opcode < OpcodeLimit && Consumed[opcode];
    /// <summary>Internal commands of the original server; never accepted from clients.</summary>
    public static bool IsInternal(ushort opcode) => opcode < OpcodeLimit && Internal[opcode];

    private static void Set(RouteMode mode, params ushort[] opcodes)
    {
        foreach (var opcode in opcodes) Modes[opcode] = mode;
    }

    public static RouteMode Mode(ushort opcode) => opcode < OpcodeLimit ? Modes[opcode] : RouteMode.None;
    public static bool IncludeSender(ushort opcode) => opcode < OpcodeLimit && IncludesSender[opcode];
}
