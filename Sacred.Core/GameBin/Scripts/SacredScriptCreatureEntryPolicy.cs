namespace Sacred.Core.GameBin.Scripts;

/// <summary>CreateNPC's pre-placement entry gate, Gold 0x482E09..0x482E49.
/// Network query 0x7F4940(0) reads the local NETWORK_SPEEDSETTINGS selector.</summary>
public static class SacredScriptCreatureEntryPolicy
{
    public static bool SkipMultiplayerEntry(SacredScriptCreateCreature creation, bool isEntering,
        uint resolvedName, uint networkSpeedSettings) => !creation.HasExplicitBehavior && isEntering &&
        creation.Aggression < 1 && unchecked((int)resolvedName) < 1 && networkSpeedSettings == 1;
}
