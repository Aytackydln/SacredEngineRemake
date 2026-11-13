namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared initial actor notifications. Other KI, palette and event
/// states require their complete native dispatch.</summary>
public static class WorldCreatureConstructionEvents
{
    /// <summary>0x555E00 emits a cEventParam (family 7) with kind 0x107,
    /// parameter tag zero and the slot at +0x14. For initial KI mode zero,
    /// 0x52E590 takes its unhandled-kind return: no recalculation or model work.
    /// Do not synthesize a CalcResults call for this notification.</summary>
    public static void EquipmentChanged(ushort kiMode, ushort slot)
    {
        if (kiMode != 0) throw new NotSupportedException("Other KI states require their actual equipment-notification dispatch.");
        if (slot >= 19) throw new ArgumentOutOfRangeException(nameof(slot));
    }

    /// <summary>Canonical SetLevel: family 7, kind 6, subcommand zero.
    /// Physical 0x44BCD0 ignores family 7. Creature 0x52EC85 with KI zero
    /// skips parameter updates and calls 0x402490(mask,255); zero returns zero
    /// without reading palettes. Graphics context creation is a separate service.</summary>
    public static uint LevelChanged(bool creatureHandler, ushort kiMode, uint paletteMask)
    {
        if (!creatureHandler) return paletteMask;
        if (kiMode != 0 || paletteMask != 0)
            throw new NotSupportedException("Non-constructor KI/palette state requires the complete creature event and palette services.");
        return 0;
    }
}
