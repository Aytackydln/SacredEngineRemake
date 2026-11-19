using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.GameRes;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>The native pre-placement entry return. This read-only preflight
/// cannot reserve names, sample a position, allocate actors or supply levels.</summary>
internal static class WorldScriptEnteredCreatureGate
{
    public static bool CanSkip(WorldScriptExecutionContext context, SacredScriptCommand command,
        uint connectionSetting, GoldScriptResourceReplica resources, GameResStore? text)
    {
        var creation = SacredScriptCreatureCreationReader.Read(command,
            _ => throw new NotSupportedException("Entry preflight requires literal position inputs."), context.Variables.Find);
        var name = WorldScriptCreatureResourceNames.Resolve(creation.Name, resources, text);
        return SacredScriptCreatureEntryPolicy.SkipMultiplayerEntry(creation, true, name, connectionSetting);
    }
}
