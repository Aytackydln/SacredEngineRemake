using System.Buffers.Binary;
using System.Text.Json;
using System.Security.Cryptography;
using Sacred.Assets.Executable;
using Sacred.GameplayHost.Networking;
using Sacred.Network.Admission;

internal static class RosterIdentifierChecks
{
    public static byte[] WireExample(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-roster-identifier.json")));
        return Convert.FromHexString(native.RootElement.GetProperty("rosters")[0].GetProperty("table_hex").GetString()!);
    }

    public static int Run(string root)
    {
        using var native = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,
            "docs/game-server/evidence/native-roster-identifier.json")));
        var data = native.RootElement;
        var image = new byte[data.GetProperty("image_size").GetInt32()];
        foreach (var chunk in data.GetProperty("definition_chunks").EnumerateArray())
            Convert.FromHexString(chunk.GetProperty("hex").GetString()!).CopyTo(image, chunk.GetProperty("offset").GetInt32());
        var checks = 0;
        var definitions = SacredRosterIdentifierDefinitions.Read(image);
        var rules = data.GetProperty("rules").EnumerateArray().ToArray();
        Check(definitions.Markup.Removals.Count == rules.Length, "original ordered pass count");
        for (var i = 0; i < rules.Length; i++)
            Check(definitions.Markup.Removals[i].Prefix == Text(rules[i].GetProperty("prefix_hex")) &&
                definitions.Markup.Removals[i].RemovalLength == rules[i].GetProperty("length").GetInt32(),
                "literal and shared length operands read from original PE sections");
        Compare(data.GetProperty("cases"), definitions);
        foreach (var row in data.GetProperty("rosters").EnumerateArray())
        {
            var previous = new byte[GoldPlayerRosterLayout.Size];
            Convert.FromHexString(row.GetProperty("staging_hex").GetString()!).CopyTo(previous, GoldPlayerDataLayout.ProfileOffset);
            var incoming = GoldPlayerRoster.FromPayload(Convert.FromHexString(row.GetProperty("table_hex").GetString()!));
            var stage = WorldRosterApplication.Apply(GoldPlayerRoster.FromPayload(previous), incoming, 16, true, true, definitions.Markup.Strip);
            Check(stage.Rows.Select(value => value.Name).SequenceEqual(row.GetProperty("names").EnumerateArray().Select(Text)),
                "native display names remain separate from stripped identifiers");
            Check(stage.Rows.Select(value => value.Identifier).SequenceEqual(row.GetProperty("identifiers").EnumerateArray().Select(Text)),
                "complete native roster consumer with actual identifier conversion");
            Check(Convert.ToHexString(SHA256.HashData(stage.Roster.ToPayload())).Equals(row.GetProperty("roster_sha256").GetString(), StringComparison.OrdinalIgnoreCase),
                "identifier conversion leaves raw roster/profile bytes intact");
            Check(stage.JoinNotices.SequenceEqual(row.GetProperty("notices").EnumerateArray().Select(value => value.GetByte())),
                "formatting conversion does not change native presence/join gates");
        }
        foreach (var variant in data.GetProperty("variants").EnumerateArray())
        {
            var changed = (byte[])image.Clone();
            foreach (var patch in variant.GetProperty("patches").EnumerateArray())
                Convert.FromHexString(patch.GetProperty("hex").GetString()!).CopyTo(changed, patch.GetProperty("offset").GetInt32());
            Compare(variant.GetProperty("cases"), SacredRosterIdentifierDefinitions.Read(changed));
        }
        // Appending another real prologue must not silently select the first caller.
        var duplicate = image.Concat(Sacred.Core.Executable.Layouts.SacredExecutableRosterIdentifierLayout.Prologue.ToArray()).ToArray();
        try { SacredRosterIdentifierDefinitions.Read(duplicate); throw new Exception("ambiguous identifier accepted"); }
        catch (InvalidDataException) { checks++; }
        var relocatedHandler = (byte[])image.Clone();
        var signatureAt = relocatedHandler.AsSpan().IndexOf(Sacred.Core.Executable.Layouts.SacredExecutableRosterIdentifierLayout.Prologue);
        BinaryPrimitives.WriteUInt32LittleEndian(relocatedHandler.AsSpan(signatureAt - Sacred.Core.Executable.Layouts.SacredExecutableRosterIdentifierLayout.PrologueOffset + 3), 0xdeadbeef);
        Check(SacredRosterIdentifierDefinitions.Read(relocatedHandler).Markup.Removals.SequenceEqual(definitions.Markup.Removals),
            "exception-handler relocation does not select or alter text rules");
        return checks;

        void Compare(JsonElement cases, SacredRosterIdentifierDefinitions original)
        {
            foreach (var row in cases.EnumerateArray())
                Check(original.Markup.Strip(Text(row.GetProperty("input_hex"))) == Text(row.GetProperty("output_hex")),
                    "complete original conversion with raw UTF16, pass order, whole-string gate and suffix clamp");
        }
        void Check(bool pass, string why)
        { if (!pass) throw new Exception("Roster identifier: " + why); checks++; }
    }

    private static string Text(JsonElement hex)
    {
        var bytes = Convert.FromHexString(hex.GetString()!);
        var chars = new char[bytes.Length / 2];
        for (var i = 0; i < chars.Length; i++) chars[i] = (char)BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i * 2));
        return new string(chars);
    }
}
