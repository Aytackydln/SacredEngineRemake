using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.GameplayHost.Scripting;
using Sacred.Network.Gameplay;

internal static class RegionQuestChecks
{
    public static byte[] WireExample(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-region-quest.json")));
        var publication = fixture.RootElement.GetProperty("cases").EnumerateArray()
            .SelectMany(row => row.GetProperty("publications").EnumerateArray()).First();
        var payload = new byte[296];
        BinaryPrimitives.WriteUInt32LittleEndian(payload, publication.GetProperty("index").GetUInt32());
        Convert.FromHexString(publication.GetProperty("row_hex").GetString()!).CopyTo(payload, 4);
        return payload;
    }

    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-region-quest.json")));
        var data = fixture.RootElement;
        var functions = Enumerable.Repeat(new SacredScriptFunction("", 0, 0, -1, 0, 0), data.GetProperty("function_count").GetInt32()).ToArray();
        foreach (var row in data.GetProperty("functions").EnumerateArray())
        {
            var function = SacredScriptFunctionDefinition.FromPayload(Convert.FromHexString(row.GetProperty("hex").GetString()!));
            functions[row.GetProperty("index").GetInt32()] = new(function.Name, function.Start, function.ByteLength, function.Quest, function.Number, function.Deleted);
        }
        var checks = 0;
        foreach (var row in data.GetProperty("cases").EnumerateArray())
        {
            var command = Convert.FromHexString(row.GetProperty("command_hex").GetString()!);
            functions[^1] = new("controlled region caller", 0, command.Length, -1, 0, 0);
            var code = new byte[functions.Max(function => function.Start + function.ByteLength)];
            command.CopyTo(code, 0);
            var quests = new WorldScriptQuests(Convert.FromHexString(row.GetProperty("before_hex").GetString()!).Chunk(292).Select(bytes => SacredScriptQuestDefinition.FromPayload(bytes)));
            var context = new WorldScriptExecutionContext(new(), functions, code, true) { Quests = quests, LocalPlayerSlot = 1 };
            var executor = new WorldScriptExecutor(context);
            if (row.GetProperty("startup").GetInt32() == 1) executor.ExecuteStartup(command);
            else
            {
                executor.ExecuteStartup(ReadOnlyMemory<byte>.Empty);
                Check(WorldScriptRegionCallbackProfile.TryInvoke(context, (ushort)(functions.Length - 1), default, out _), "ordinary region quest profile");
            }
            Check(Enumerable.Range(0, quests.Count).SelectMany(index => quests.Get(index).ToPayload()).SequenceEqual(
                Convert.FromHexString(row.GetProperty("after_hex").GetString()!)), "all native changed/preserved quest bytes");
            Check(quests.Scratch!.SequenceEqual(new uint[5]), "native entry scratch clearing");
            var expected = row.GetProperty("publications");
            Check(quests.ActivationPublications.Count == expected.GetArrayLength(), "startup/active-row suppression");
            for (var index = 0; index < expected.GetArrayLength(); index++)
            {
                var publication = quests.ActivationPublications[index]; var source = expected[index];
                Check(publication.Opcode == 0x1ba && BinaryPrimitives.ReadUInt32LittleEndian(publication.Payload) == source.GetProperty("index").GetUInt32() &&
                    publication.Payload.AsSpan(4).SequenceEqual(Convert.FromHexString(source.GetProperty("row_hex").GetString()!)), "complete ordinary publication and five native callback hashes");
                Check(GoldQuestCodec.Decode(publication) is GoldQuestUpdateEvent, "existing production decoder accepts native-derived request body");
            }
            Check(!context.IsExecutingStartup && !context.IsInitializingRegion && !context.IsInvalidated, "normal scope restored/retained");
        }
        using var bank = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-quest-bank-source.json")));
        // Bank output is tested through the installed lifetime below. This
        // replay compares its shared native definition conversion independently
        // from callback invocation and slot/transport prerequisites.
        var bankFunctions = Enumerable.Repeat(new SacredScriptFunction("", 0, 0, -1, 0, 0), bank.RootElement.GetProperty("function_count").GetInt32()).ToArray();
        foreach (var function in bank.RootElement.GetProperty("functions").EnumerateArray())
            bankFunctions[function.GetProperty("index").GetInt32()] = new(System.Text.Encoding.Latin1.GetString(
                Convert.FromHexString(function.GetProperty("name_hex").GetString()!)).Split('\0')[0], 0, 0, -1, 0, 0);
        foreach (var row in bank.RootElement.GetProperty("cases").EnumerateArray())
        {
            var quests = new WorldScriptQuests(Convert.FromHexString(row.GetProperty("before_hex").GetString()!).Chunk(292).Select(bytes => SacredScriptQuestDefinition.FromPayload(bytes)));
            var context = new WorldScriptExecutionContext(new(), bankFunctions, ReadOnlyMemory<byte>.Empty, true) { Quests = quests };
            new WorldScriptExecutor(context).ExecuteStartup(ReadOnlyMemory<byte>.Empty);
            // Access to private source composition is via the same public
            // quest serializer used by the host, without adding wire grants.
            var actual = quests.WriteBankSection(context);
            Check(actual.SequenceEqual(Convert.FromHexString(row.GetProperty("wire_hex").GetString()!)), "complete native quest bank section");
            Check(Enumerable.Range(0, quests.Count).SelectMany(index => quests.Get(index).ToPayload()).SequenceEqual(Convert.FromHexString(row.GetProperty("before_hex").GetString()!)), "bank conversion cannot mutate interpreter callback indices");
        }
        return checks;
        void Check(bool pass, string why) { if (!pass) throw new Exception("Region quest: " + why); checks++; }
    }
}
