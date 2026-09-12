using System.Text;
using Sacred.Core.Pak.Items;

namespace ParticleEmitterDataset;

internal static class ExecutableEvidence
{
    public static Dictionary<uint, string> TypeNames(string game, ItemsPakEntry[] items, string output)
    {
        var bytes = File.ReadAllBytes(Path.Combine(game, "Sacred.exe"));
        var anchor = items.Single(i => i.ModelName == "SimpleLightSmall");
        var offset = bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes("TYPE_SIMPLE_LIGHT_SMALL\0"));
        if (offset < 0) throw new InvalidDataException("Executable type catalogue anchor missing");
        var start = offset - anchor.ItemIndex * 0x44;
        var names = new Dictionary<uint, string>();
        var rows = new List<object>();
        foreach (var item in items)
        {
            var fileOffset = start + item.ItemIndex * 0x44;
            if (fileOffset < 0 || fileOffset + 0x44 > bytes.Length) continue;
            var slot = bytes.AsSpan(fileOffset, 0x44); var end = slot.IndexOf((byte)0);
            if (end < 0) continue;
            var name = Encoding.ASCII.GetString(slot[..end]);
            if (!name.StartsWith("TYPE_")) continue;
            names[item.ItemIndex] = name;
            rows.Add(new { item_id = item.ItemIndex, item.ModelName, type_name = name,
                file_offset = fileOffset, raw_slot_hex = Convert.ToHexString(slot),
                association_basis = "Existing probe's 0x44-stride name catalogue, anchored on TYPE_SIMPLE_LIGHT_SMALL and Items model SimpleLightSmall; not a particle-parameter table." });
        }
        JsonOutput.Lines(Path.Combine(output, "Sacred.exe.type-names.jsonl"), rows);
        JsonOutput.Write(Path.Combine(output, "Sacred.exe.type-table.json"), new { file_offset = start, slot_stride = 0x44,
            anchor_item_id = anchor.ItemIndex, matched_type_names = names.Count,
            caution = "Names provide research leads; adjacent bytes and numeric IDs are not established emitter parameters." });
        return names;
    }
}
