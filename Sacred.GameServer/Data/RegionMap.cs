using System.Runtime.InteropServices;
using System.Text;
using Sacred.Core.World;

namespace Sacred.GameServer.Data;

/// <summary>
/// The game's regions (Ancaria's countries, the ones the region counter and the Region Boss quest count in) and which sector lies in
/// which region, from the game files: no client is needed to know where a sector is.
/// <list type="bullet">
/// <item><c>World/sectors.keyx</c>: the 0x300-byte sector records (header 0x100 bytes); the byte at record offset 0x2C0 is the sector's region
/// code (the byte the game reads at +0xD7 of the sector's environment block).</item>
/// <item><c>bin/Balance.bin</c>: the 0x40-byte region records the game copies into its region table: +0x00 up to 16 region codes, +0x10 the name
/// key (<c>UI_REGION_SOUTHKERN</c>), +0x30 kills needed, +0x34 boss flag, +0x38 kills left, +0x3C region id. The game takes the FIRST region
/// whose codes hold a sector's code (0x4254A0): codes that two regions list belong to the first.</item>
/// </list>
/// The name key is resolved with the game's text table when one is given. Immutable once built; safe from any thread.
/// </summary>
public sealed class RegionMap
{
    private const int RecordHeader = 0x100, RecordSize = 0x300, CodeOffset = 0x2C0, BalanceRecord = 0x40;

    public sealed record Region(int Id, string Key, string Name, byte[] Codes, int KillsNeeded, bool Boss);

    public required IReadOnlyList<Region> Regions { get; init; }
    /// <summary>Sector id to region id; sectors in no region are not in it.</summary>
    public required IReadOnlyDictionary<uint, int> RegionOfSector { get; init; }
    /// <summary>Sector id to its region code (every sector).</summary>
    public required IReadOnlyDictionary<uint, byte> CodeOfSector { get; init; }
    /// <summary>Region id to its sector ids, ascending.</summary>
    public required IReadOnlyDictionary<int, uint[]> SectorsOf { get; init; }

    public int RegionAt(uint sector) => RegionOfSector.TryGetValue(sector, out var id) ? id : 0;

    public static RegionMap Load(string gameDirectory, Func<string, string?>? nameOfKey = null)
    {
        var regions = ReadRegions(File.ReadAllBytes(Path.Combine(gameDirectory, "bin", "Balance.bin")), nameOfKey);
        var codes = ReadSectorCodes(File.ReadAllBytes(Path.Combine(gameDirectory, "World", "sectors.keyx")));
        return Build(regions, codes);
    }

    /// <summary>The region records of Balance.bin: found by their name keys, which all start with UI_REGION_.</summary>
    public static List<Region> ReadRegions(byte[] balance, Func<string, string?>? nameOfKey = null)
    {
        var list = new List<Region>();
        var prefix = "UI_REGION_"u8;
        var at = balance.AsSpan().IndexOf(prefix);
        if (at < 0x10) return list;
        at -= 0x10;
        while (at + BalanceRecord <= balance.Length && balance.AsSpan(at + 0x10, prefix.Length).SequenceEqual(prefix))
        {
            var record = balance.AsSpan(at, BalanceRecord);
            var codes = record[..0x10].ToArray().Where(b => b != 0).ToArray();
            var keySpan = record.Slice(0x10, 0x20);
            var end = keySpan.IndexOf((byte)0);
            var key = Encoding.ASCII.GetString(end >= 0 ? keySpan[..end] : keySpan);
            var kills = MemoryMarshal.Read<uint>(record[0x30..]);
            var boss = MemoryMarshal.Read<uint>(record[0x34..]);
            var id = (int)MemoryMarshal.Read<uint>(record[0x3C..]);
            var name = nameOfKey?.Invoke(key);
            list.Add(new Region(id, key, string.IsNullOrWhiteSpace(name) ? Prettify(key) : name.Trim(), codes, (int)kills, boss != 0));
            at += BalanceRecord;
        }
        return list;
    }

    /// <summary>"UI_REGION_SOUTHKERN" as "Southkern": a readable fallback while the game text is not available.</summary>
    public static string Prettify(string key)
    {
        var word = key.StartsWith("UI_REGION_", StringComparison.Ordinal) ? key["UI_REGION_".Length..] : key;
        word = word.Replace('_', ' ').ToLowerInvariant();
        return word.Length == 0 ? key : char.ToUpperInvariant(word[0]) + word[1..];
    }

    /// <summary>Sector id and region code of every record of a sectors.keyx.</summary>
    public static List<(uint Id, byte Code)> ReadSectorCodes(byte[] keyx)
    {
        var list = new List<(uint, byte)>();
        if (keyx.Length < RecordHeader || keyx[0] != (byte)'W' || keyx[1] != (byte)'L' || keyx[2] != (byte)'K')
            throw new InvalidDataException("not a sectors.keyx");
        var count = (int)MemoryMarshal.Read<uint>(keyx.AsSpan(4));
        if (count < 0 || count > (keyx.Length - RecordHeader) / RecordSize) throw new InvalidDataException("sectors.keyx has a truncated record array");
        for (var i = 0; i < count; i++)
        {
            var at = RecordHeader + i * RecordSize;
            var id = MemoryMarshal.Read<uint>(keyx.AsSpan(at + 0x24));
            if (id != 0) list.Add((id, keyx[at + CodeOffset]));
        }
        return list;
    }

    /// <summary>Joins the two: a sector's region is the first listed region that holds its code.</summary>
    public static RegionMap Build(IReadOnlyList<Region> regions, IEnumerable<(uint Id, byte Code)> sectors)
    {
        var byCode = new Dictionary<byte, int>();
        foreach (var region in regions)
            foreach (var code in region.Codes) byCode.TryAdd(code, region.Id);
        var regionOf = new Dictionary<uint, int>();
        var codeOf = new Dictionary<uint, byte>();
        var members = new SortedDictionary<int, List<uint>>();
        foreach (var (id, code) in sectors)
        {
            codeOf[id] = code;
            if (!byCode.TryGetValue(code, out var region)) continue;
            regionOf[id] = region;
            if (!members.TryGetValue(region, out var list)) members[region] = list = [];
            list.Add(id);
        }
        return new RegionMap
        {
            Regions = regions, RegionOfSector = regionOf, CodeOfSector = codeOf,
            SectorsOf = members.ToDictionary(kv => kv.Key, kv => kv.Value.Order().ToArray()),
        };
    }
}
