using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Executable;
using Sacred.Assets.GameBin.Balance;
using Sacred.Core.GameBin.Balance;
using Sacred.Core.Pak.Creature;
using Sacred.GameplayHost.Scripting;

internal static class CreatureLevelSelectionChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-level-selection.json")));
        var fixture = source.RootElement;
        var original = Convert.FromHexString(fixture.GetProperty("balance_prefix_hex").GetString()!);
        // The fixture retains the native counted global block; unrelated maps follow it in the file.
        var overrideCode = Convert.FromHexString(fixture.GetProperty("cap_override_code_hex").GetString()!);
        var definitions = SacredCreatureLevelDefinitions.Read(overrideCode);
        var overrideType = BinaryPrimitives.ReadUInt32LittleEndian(overrideCode.AsSpan(8));
        Check(definitions.MaximumOverride(overrideType) == BinaryPrimitives.ReadUInt32LittleEndian(overrideCode.AsSpan(15)), "Original resource cap extraction.");
        Check(definitions.MaximumOverride(overrideType + 1) is null, "Unlisted resources have no special cap.");
        var changedCode = overrideCode.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(changedCode.AsSpan(8), 123456);
        BinaryPrimitives.WriteUInt32LittleEndian(changedCode.AsSpan(15), 321);
        Check(SacredCreatureLevelDefinitions.Read(changedCode).MaximumOverride(123456) == 321, "Resource and cap come from instructions.");
        try { SacredCreatureLevelDefinitions.Read(overrideCode.Concat(overrideCode).ToArray()); throw new Exception("Ambiguous level cap accepted."); }
        catch (InvalidDataException) { checks++; }

        foreach (var row in fixture.GetProperty("cases").EnumerateArray())
        {
            uint U(string name) => row.GetProperty(name).GetUInt32();
            bool B(string name) => row.GetProperty(name).GetBoolean();
            var bytes = original.ToArray();
            foreach (var tuning in row.GetProperty("tuning").EnumerateObject())
                BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4 + int.Parse(tuning.Name)), tuning.Value.GetUInt32());
            var balance = CreatureBalanceBinReader.Read(bytes);
            var levels = CreatureLevelBalanceBinReader.Read(bytes);
            var skills = row.GetProperty("skills").EnumerateArray().Select(value => value.GetByte()).ToArray();
            var template = Template(B("horse") ? SacredCreatureClass.Horse : SacredCreatureClass.Mercenary, (ushort)U("template_flags"), skills);
            var context = new WorldCreatureLevelContext(U("reference"), U("difficulty"), B("multiplayer"),
                U("behavior"), (byte)U("actor_flags"), B("sector_present") ? (ushort)U("sector_maximum") : (ushort)0,
                U("runtime_addition"), B("skip"), B("ignore"), B("override") ? definitions.MaximumOverride(overrideType) : null);
            var result = WorldCreatureLevel.Resolve((ushort)U("input"), template, context, levels, balance);
            Check(result.CalculatedLevel == U("calculated_level") && result.Level == U("level"), $"Native level selection differs: {row}");
            Check(result.Experience == U("experience"), $"Native experience differs: {row}");
            if (result.Level == 0 || result.Level > 250) continue; // Health calculation has a separate domain/oracle.
            var progression = new WorldCreatureProgression(template);
            progression.SetLevel((ushort)U("input"), context, levels, balance, players: 1);
            var values = progression.Write(0x16F);
            Check(values.Level == result.Level && values.Experience == result.Experience, "Selected level/experience reach compact construction values.");
            Check(values.Block2C.SequenceEqual(row.GetProperty("ranks").EnumerateArray().Select(value => value.GetByte()).ToArray()),
                "Native skill update gate/rank selection reaches compact construction values.");
        }

        // Verify the documented file aliases using deliberately distinct bytes.
        var alias = original.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(alias.AsSpan(4 + SacredCreatureLevelBalanceLayout.MultiplayerAdditionOffset), 123);
        var aliased = CreatureLevelBalanceBinReader.Read(alias);
        Check(aliased.InputAdditions[4] == 123 && aliased.MultiplayerAddition == 123, "Original overlapping global storage retained.");
        foreach (var length in new[] { 0, 3, 4 + SacredCreatureLevelBalanceLayout.RequiredGlobalBytes - 1 })
        {
            var truncated = original.AsSpan(0, length).ToArray();
            if (length >= 4) BinaryPrimitives.WriteUInt32LittleEndian(truncated, (uint)length - 4);
            try { CreatureLevelBalanceBinReader.Read(truncated); throw new Exception("Truncated creature level tuning accepted."); }
            catch (InvalidDataException) { checks++; }
        }
        return checks;
    }

    private static SacredCreatureTemplate Template(SacredCreatureClass kind, ushort flags, byte[] skills) =>
        new(0, 0, CreatureGoldRecordLayout.Size, 42, kind, 0, flags, 0, 0,
            20, 20, 20, 20, 20, 20, 0, 100, 100, skills[..2], skills[2..], [], []);
}
