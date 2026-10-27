using System.Buffers.Binary;
using System.Text.Json;
using Sacred.Core.Pak.Weapon;

internal static class EquipmentVariantNormalizationChecks
{
    public static int Run(string root)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-equipment-variant-normalization.json")));
        var checks = 0;
        foreach (var row in fixture.RootElement.GetProperty("cases").EnumerateArray())
        {
            var input = row.GetProperty("inputs");
            var values = new byte[SacredEquipmentInstanceLayout.Size];
            BinaryPrimitives.WriteUInt32LittleEndian(values, 1000);
            BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(4), input.GetProperty("flags").GetUInt32());
            uint resource = 1000;
            var trace = new List<object[]>();
            var success = SacredEquipmentVariantNormalization.Apply(values, resource, input.GetProperty("actor").GetUInt32(),
                input.GetProperty("manager").GetBoolean() && input.GetProperty("shared").GetBoolean() ? input.GetProperty("set_type").GetByte() : null,
                () =>
                {
                    trace.Add(["variant"]);
                    var replacement = input.GetProperty("replacement").GetUInt32();
                    return new(replacement != 0x7E60, replacement == 0x7E60 ? 0 : replacement);
                }, replacement =>
                {
                    resource = replacement;
                    trace.Add(["reset", resource, BinaryPrimitives.ReadUInt32LittleEndian(values), BinaryPrimitives.ReadUInt32LittleEndian(values.AsSpan(4))]);
                    if (input.GetProperty("reset_flags").ValueKind != JsonValueKind.Null)
                        BinaryPrimitives.WriteUInt32LittleEndian(values.AsSpan(4), input.GetProperty("reset_flags").GetUInt32());
                }, () =>
                {
                    trace.Add(["values"]);
                    return !input.GetProperty("values").GetBoolean() ? null : input.GetProperty("external").GetBoolean()
                        ? input.GetProperty("external_flags").GetUInt32() : BinaryPrimitives.ReadUInt32LittleEndian(values.AsSpan(4));
                });
            if (success != row.GetProperty("result").GetBoolean() || resource != row.GetProperty("resource").GetUInt32() ||
                BinaryPrimitives.ReadUInt32LittleEndian(values) != row.GetProperty("value_resource").GetUInt32() ||
                BinaryPrimitives.ReadUInt32LittleEndian(values.AsSpan(4)) != row.GetProperty("flags").GetUInt32() ||
                row.GetProperty("external_flags").GetUInt32() != input.GetProperty("external_flags").GetUInt32())
                throw new Exception($"Native variant normalization differs: {row}; result {success}, resource {resource}, own bytes {Convert.ToHexString(values)}.");
            // Native repeats a pure value-view getter. Compare its first ordered
            // access; all lookups in this fixture resolve the same stable view.
            var seenValues = false;
            var expected = row.GetProperty("trace").EnumerateArray().Where(effect =>
            {
                if (effect[0].GetString() != "values") return true;
                if (seenValues) return false;
                seenValues = true;
                return true;
            }).ToArray();
            if (JsonSerializer.Serialize(trace) != JsonSerializer.Serialize(expected))
                throw new Exception($"Native normalization order differs: {row}; actual {JsonSerializer.Serialize(trace)}.");
            checks += 6;
        }
        return checks;
    }
}
