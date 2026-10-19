using System.Buffers.Binary;
using Sacred.Assets.GameBin;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Networking;

/// <summary>The post-callback effects required by Gold 0x4939F0. Retaining these
/// inputs does not create actors or count the native operation as completed.</summary>
public sealed record WorldParticipantSectorPopulation(ushort Sector, bool RequiresAmbientGeneration,
    IReadOnlyList<byte[]> RestoredObjects, IReadOnlyList<SacredScriptSpawnEquipment> EquipmentRules)
{
    public WorldScriptSectorEnvironment? Environment { get; init; }
    public bool RequiresExecution => RequiresAmbientGeneration || RestoredObjects.Count != 0;

    internal static WorldParticipantSectorPopulation Read(WorldScriptSectorEnvironment sector,
        GoldWorldBankSnapshot bank, IReadOnlyList<SacredScriptSpawnEquipment> equipment)
    {
        if (bank.Opcode != GoldWorldBankLayout.Bank3Opcode) throw new ArgumentException("Expected bank 3.", nameof(bank));
        var environment = sector.Value;
        // Gold 0x4942A6..0x4942C6 excludes its ambient scan when either
        // original liquid selector lies in the inclusive range 4..6.
        var ambient = environment.LiquidStyleA is not (>= 4 and <= 6) && environment.LiquidStyleB is not (>= 4 and <= 6);
        var objects = bank.Sections[4].Records
            .Where(row => BinaryPrimitives.ReadUInt32LittleEndian(row.Data[GoldSectorObjectRestoreLayout.SectorOffset..]) == sector.Sector)
            .Select(row => row.Data.ToArray()).ToArray();
        return new(sector.Sector, ambient, Array.AsReadOnly(objects), Array.AsReadOnly(equipment.ToArray())) { Environment=sector };
    }

    public override string ToString() => $"sector {Sector}: native population pending (ambient generation {RequiresAmbientGeneration}, " +
        $"{RestoredObjects.Count} stored objects, {EquipmentRules.Count} spawn-equipment rules)";
}
