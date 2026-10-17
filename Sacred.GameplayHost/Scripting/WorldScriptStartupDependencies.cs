using Sacred.Assets.GameBin;
using Sacred.Assets.Paks.Creature;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.World;

namespace Sacred.GameplayHost.Scripting;

/// <summary>File-derived diagnostics for the first unresolved factory; never advances execution coverage.</summary>
internal static class WorldScriptStartupDependencies
{
    public static WorldScriptCreatureBlueprint? InspectCreature(string installation, WorldCampaignScripts scripts,
        WorldScriptExecutionContext state, SacredObjectTemplateArchive templates, Action<string> log)
    {
        if (state.Failure is not { Opcode: SacredScriptCreateCreature.Opcode } failure) return null;
        var code = failure.Script == "StartCode.bin" ? scripts.StartupCode : scripts.FunctionCode;
        try
        {
            var command = SacredCompiledScriptReader.Read(code).First(entry => entry.FileOffset == failure.Offset);
            var creatures = CreaturePakArchive.Load(Path.Combine(installation, "PAK", "Creature.pak"));
            var blueprint = WorldScriptCreatureBlueprint.Read(state, command, templates, creatures);
            var creation = blueprint.Creation;
            var grid = SacredSectorIndex.Read(Path.Combine(installation, "World", "sectors.keyx")).CreateGrid();
            var placement = WorldScriptCreaturePlacement.Resolve(creation, grid);
            log($"Host startup unresolved creature: resource {creation.TypeId}, Creature.pak record {blueprint.Template.RecordIndex} " +
                $"at 0x{blueprint.Template.RecordOffset:X}, class {blueprint.Template.Class}; tile {creation.TilePosition}, world operand {creation.WorldPosition}, native placement {placement}, " +
                $"group {creation.Group}, behavior 0x{creation.Behavior:X8}, level input {creation.LevelInput}; " +
                $"equipment resources [{string.Join(", ", creation.EquipmentTypes)}].");
            log("Host startup creature dependencies: native constructor 0x52DE40 -> character seed 0x55E1B0/0x586C00; " +
                "level calculation 0x564D60; final values 0x5796A0; equipment construction/installation 0x5D5080/0x555E00. " +
                "Decoded blueprint only; no creature/equipment references allocated or runtime effects counted.");
            return blueprint;
        }
        catch (InvalidDataException error)
        {
            log($"Host startup creature source dependency: {error.Message}");
            return null;
        }
    }
}
