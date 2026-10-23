using System.Buffers.Binary;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Sacred.Core.World;
using Sacred.Core.World.Pathing;
using Sacred.Core.World.Sector;
using Sacred.Network.Gameplay;
using Sacred.World.Networking;

internal static class SectorPatchChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private static int _checks;
    internal static void Check(bool value, string name)
    {
        if (!value) throw new Exception("Sector patches: " + name);
        _checks++;
    }
    public static int Run(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/game-server/evidence/native-sector-patches.json")));
        var records = document.RootElement.GetProperty("static_records_hex").EnumerateArray()
            .Select(row => MemoryMarshal.Read<StaticObjectRecord>(Convert.FromHexString(row.GetString()!))).ToArray();
        Func<uint, StaticObjectRecord?> read = id => id is >= 1 and <= 4 ? records[id - 1] : null;
        foreach (var sample in document.RootElement.GetProperty("samples").EnumerateArray())
        {
            var seed = sample.GetProperty("seed").GetInt32();
            var anchor = seed % 2 == 0 ? 20 : 5;
            var source = Sector();
            var planes = new[] { source.Pathing, new WorldPathingLayer(64, 64), new WorldPathingLayer(64, 64) };
            for (var level = 0; level < 3; level++)
            for (var i = 0; i < 4096; i++) planes[level][i % 64, i / 64] = new((WorldPathFlags)((i * 13 + seed + level * 7) & 127), default);
            for (var y = 10; y < 12; y++)
            for (var x = 10; x < 15; x++) source.TileAnchorDeltas![x, y] = ((sbyte)(anchor - x), (sbyte)(anchor - y));
            source.StaticChainHeads![anchor, anchor] = 1;
            var flags = source.Pathing[anchor, anchor].Flags;
            source.Pathing[anchor, anchor] = new(sample.GetProperty("anchor_indoor").GetBoolean() ? flags | WorldPathFlags.Indoor : flags & ~WorldPathFlags.Indoor, default);
            for (var level = 1; level <= 2; level++) source.IndoorTileGroups.Add(new(new(source.Coord, level - 1), 0, 0, 64, 64,
                default, (byte)level, planes[level], new(64, 64), [])); // Clear records remain valid native floor cells.
            var copiesType = typeof(SacredNetworkWorldBinding).Assembly.GetType("Sacred.World.Networking.SacredNetworkSectorCopies", true)!;
            var copies = Activator.CreateInstance(copiesType, true)!;
            var sector = (Sector)copiesType.GetMethod("Create")!.Invoke(copies, [source])!;
            copiesType.GetMethod("Synchronize")!.Invoke(copies, [sector]);
            var sectors = new Dictionary<ushort, Sector> { [1] = sector };
            var patches = (SacredNetworkSectorPatches)Activator.CreateInstance(typeof(SacredNetworkSectorPatches), Hidden, null, [read, null], null)!;
            var bank3 = Bank(0x1B3, 5, Convert.FromHexString(sample.GetProperty("patches_hex").GetString()!), 88);
            var bank1 = Bank(0x1B1, 2, Convert.FromHexString(sample.GetProperty("positions_hex").GetString()!), 16);
            var notified = 0; patches.SectorEntered += id => { Check(id == 1, "local event sector"); notified++; };
            var before = planes.Select(Hash).ToArray();
            for (var repeat = 0; repeat < 2; repeat++)
            {
                Check((bool)typeof(SacredNetworkSectorPatches).GetMethod("Apply", Hidden)!.Invoke(patches, [sector, sectors, bank3, bank1])!, "fixture terrain is ready");
                typeof(SacredNetworkSectorPatches).GetMethod("NotifyEntered", Hidden)!.Invoke(patches, [(ushort)1]);
                var actual = new[] { sector.Pathing }.Concat(sector.IndoorTileGroups.Groups.Select(group => group.Pathing)).ToArray();
                var expected = sample.GetProperty("result");
                for (var level = 0; level < 3; level++)
                {
                    Check(Hash(actual[level]) == expected.GetProperty("plane_sha256")[level].GetString(), $"native full plane {level}, seed {seed}");
                    Check(Hash(planes[level]) == before[level], "cached source plane unchanged");
                }
                var active = patches.Active.SelectMany(patch => patch.ToPayload()).ToArray();
                Check(Convert.ToHexString(active).Equals(expected.GetProperty("active_hex").GetString(), StringComparison.OrdinalIgnoreCase), "native registry order, duplicates and entire 88-byte preservation");
                Check(patches.EnterNotificationCount == repeat + 1 && notified == repeat + 1, "duplicate receives still notify once each");
            }
            var replacement = (Sector)copiesType.GetMethod("Create")!.Invoke(copies, [source])!;
            copiesType.GetMethod("Synchronize")!.Invoke(copies, [replacement]);
            Check(ReferenceEquals(replacement.IndoorTileGroups.Groups[0].Pathing, sector.IndoorTileGroups.Groups[0].Pathing), "later floor aliases preserve session tile effects");
        }
        return _checks;
    }
    internal static GoldWorldBankSnapshot Bank(ushort opcode, int section, byte[] data, int stride, byte[]? globals = null)
    {
        var prefix = 40 + (opcode == 0x1B1 ? GoldWorldBankGlobals.Size : 0);
        var bytes = new byte[prefix + data.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(section * 4), (uint)(data.Length / stride));
        globals?.CopyTo(bytes, 40); data.CopyTo(bytes, prefix);
        return GoldWorldBankSnapshot.FromServerPayload(opcode, bytes);
    }
    internal static string Hash(WorldPathingLayer plane)
    {
        var bytes = new byte[plane.Width * plane.Height];
        for (var y = 0; y < plane.Height; y++)
        for (var x = 0; x < plane.Width; x++) bytes[y * plane.Width + x] = (byte)plane[x, y].Flags;
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
    internal static Sector Sector(SectorCoord coord = default) => new(coord, default, new(64, 64), new(64, 64), new(), new(), new(), new(), new(),
        new(64, 64), new(64, 64), new(64, 64), new(64, 64)) { TileAnchorDeltas = new(64, 64), StaticChainHeads = new(64, 64) };
}
