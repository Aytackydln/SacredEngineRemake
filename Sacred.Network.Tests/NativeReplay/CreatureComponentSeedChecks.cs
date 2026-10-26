using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.Paks.Creature;
using Sacred.Core.Pak.Creature;
using Sacred.GameplayHost.Scripting;

internal static class CreatureComponentSeedChecks
{
    public static int Run(string root)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        using var source = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-creature-level-selection.json")));
        foreach (var row in source.RootElement.GetProperty("seeds").EnumerateArray())
        {
            var type = row.GetProperty("type").GetUInt32();
            SacredCreatureTemplate? template = null;
            if (row.GetProperty("template_hex").GetString() is { } hex)
            {
                var archive = new byte[CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size * 2];
                BinaryPrimitives.WriteUInt32LittleEndian(archive, 0x00464943);
                BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 2);
                Convert.FromHexString(hex).CopyTo(archive, CreaturePakHeaderLayout.Size + CreatureGoldRecordLayout.Size);
                template = CreaturePakArchive.Parse(archive).Entries[1];
            }
            var expected = Convert.FromHexString(row.GetProperty("inputs_hex").GetString()!);
            var seed = WorldCreatureComponentSeed.Create(type, template);
            Check(seed.AsSpan(0, expected.Length).SequenceEqual(expected), $"Native component input bytes differ for type {type}.");
            var progression = template is null ? new WorldCreatureProgression(type) : new WorldCreatureProgression(template);
            var values = progression.Write(0x16F);
            Check(values.Level == BinaryPrimitives.ReadUInt16LittleEndian(expected.AsSpan(78)) &&
                values.Experience == BinaryPrimitives.ReadUInt32LittleEndian(expected.AsSpan(4)), "Compact seed level/experience.");
            Check(values.Block24.SequenceEqual(expected.AsSpan(28, 8)) && values.Block2C.SequenceEqual(expected.AsSpan(36, 8)), "Compact seed skill IDs/ranks.");
            Check(values.Block128.SequenceEqual(Convert.FromHexString(row.GetProperty("health_hex").GetString()!)), "Constructor health defaults before calculated values.");
            try { progression.Write(0x16B); throw new Exception("Seed published as completed full values."); }
            catch (InvalidOperationException) { checks++; }
        }
        return checks;
    }
}
