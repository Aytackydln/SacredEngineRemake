using Sacred.Assets.Executable;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Gold 0x484326..0x4843C2 combined smith/trader/combo/HorseMerchant setup. During startup
/// 0x5498F0 stores the dialog without sending 0x11A. Later/live service updates
/// require their independent publication lifecycle.</summary>
public static class WorldCreatureScriptServices
{
    public static WorldCreatureInitialActor ApplyInitial(WorldCreatureInitialActor actor, uint mode,
        SacredCreatureScriptServiceDefinitions? definitions)
    {
        if (mode == 0) return actor;
        if ((mode & ~0x3Cu) != 0 || definitions is null)
            throw new NotSupportedException("Initial NPC service requires its compared merchant operands.");
        uint flags = 0;
        if ((mode & 4) != 0) flags |= Required(definitions.TraderFlags);
        if ((mode & 8) != 0) flags |= Required(definitions.ComboMasterFlags);
        if ((mode & 16) != 0) flags |= Required(definitions.SmithFlags);
        if ((mode & 32) != 0) flags |= definitions.HorseMerchantFlags;
        return actor with { Base = actor.Base with { Flags = actor.Base.Flags | definitions.DialogObjectFlags },
            RuntimeFlags = actor.RuntimeFlags | flags,
            DialogIndex = (mode & 32) != 0 ? definitions.HorseMerchantDialog : actor.DialogIndex };
    }
    private static uint Required(uint value) => value != 0 ? value
        : throw new InvalidDataException("Original merchant service initialization is unavailable.");
}
