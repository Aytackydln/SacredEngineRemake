using Sacred.GameServer.Auth.Hero;
using Sacred.GameServer.Wire;

namespace Sacred.GameServer.Tests;

/// <summary>Developer tool, skipped unless HERODUMP_DIR is set: decodes the lobby blocks snap-*.bin of that folder into snap-*.{hero,objects,inventory,shared}.bin.</summary>
[TestClass]
public class HeroDump
{
    [TestMethod]
    public void DumpSnapshots()
    {
        var dir = Environment.GetEnvironmentVariable("HERODUMP_DIR");
        if (dir is null) return;
        var inflater = new ZlibInflater();
        foreach (var path in Directory.GetFiles(dir, "snap-*.bin"))
        {
            if (path.Contains(".hero") || path.Contains(".objects") || path.Contains(".inventory") || path.Contains(".shared") || path.Contains(".file")) continue;
            var file = new byte[0x40000];
            var length = HeroFile.ReadBlock(File.ReadAllBytes(path), inflater, file);
            Assert.IsTrue(length > 0, path);
            var span = file.AsSpan(0, length);
            foreach (var (name, type) in new[] { ("hero", HeroFile.Hero), ("objects", HeroFile.Objects), ("inventory", HeroFile.Inventory), ("shared", HeroFile.SharedInventory) })
            {
                var index = HeroFile.FindChunk(span, type, out _, out _);
                if (index < 0) continue;
                var buffer = new byte[0x40000];
                var n = HeroFile.Decode(span, index, inflater, buffer);
                File.WriteAllBytes(Path.ChangeExtension(path, "." + name + ".bin"), buffer.AsSpan(0, n).ToArray());
            }
            File.WriteAllBytes(Path.ChangeExtension(path, ".file.bin"), span.ToArray());
            var oi = HeroFile.FindChunk(span, HeroFile.Objects, out _, out _);
            var obuf = new byte[0x40000];
            var on = HeroFile.Decode(span, oi, inflater, obuf);
            var list = obuf.AsSpan(0, on);
            var lines = new List<string>();
            var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(list);
            var position = 4;
            for (var i = 0; i < count && position + 4 <= list.Length; i++)
            {
                var resource = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(list[position..]);
                position += 4;
                if (resource == 0) { lines.Add($"{i} resource 0"); continue; }
                var size = SavedObjects.RecordSize(list[position..], span[3]);
                lines.Add($"{i} resource {resource:X} offset {position} size {size} item {SavedObjects.IsItem(list[position..])}");
                if (size < 0) break;
                position += size;
            }
            File.WriteAllLines(Path.ChangeExtension(path, ".txt"), lines);
        }
    }
}
