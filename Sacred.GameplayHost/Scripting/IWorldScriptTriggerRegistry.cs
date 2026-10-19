using Sacred.Core.GameBin.Scripts;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Trigger registration and tile patch effects supplied by the gameplay host.</summary>
public interface IWorldScriptTriggerRegistry
{
    IReadOnlyDictionary<uint, GoldWorldTriggerState> Records { get; }
    IReadOnlyDictionary<uint, uint> Patches { get; }
    IReadOnlyList<(ushort Sector, SacredScriptPosition Position)> PendingPositions { get; }
    uint? Find(string name);
    void Create(SacredScriptTriggerCreation creation);
    /// <summary>Gold startup 0x463F50: replace the packed cell mapping and queue tile positions.
    /// Duplicate mapping or queued XYZ ends the entire current command, including its remaining cells.</summary>
    void Patch(SacredScriptTriggerPatch patch);
}
