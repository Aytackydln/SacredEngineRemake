using System.Buffers.Binary;

namespace Sacred.GameServer.Auth.Hero;

/// <summary>
/// Inventory footprints (width x height in cells) of every item resource, computed once from the game's item tables the way
/// GameServer.exe does it. FUN_00404470(resource, &amp;w, &amp;h):
/// <list type="bullet">
/// <item>resource 0xFA7 is 2x2;</item>
/// <item>otherwise the type descriptor's word +0x18 (resource &lt; 0x7E60) indexes the Weapon.pak row table (0x102 bytes per row,
/// after a 0x100-byte header); row bytes +0x1C/+0x1D are width/height when both are nonzero;</item>
/// <item>else the descriptor's category byte +0x2E decides (jump table 0x4046E0): 5 = 1x4 for six resources, 1x3 otherwise;
/// 6 and 13 = 2x3; 16 = 1x2; 17, 18, 21, 22, 23 = 2x2; 19, 24 = 2x1; anything else 1x1.</item>
/// </list>
/// The descriptors (0x80 bytes) are built like the server's loader: Items.pak by entry index (FUN_004048c0, only entries
/// with a nonzero category), category fixes of FUN_00404aa0, the overlays Items00..Items15.pak by their target word +0x76,
/// then Weapon.pak (FUN_00406130) rewrites word +0x18 of the type named by row +0x80 to the row index (0xFD5 gets category 6)
/// and every row with a base type (row +0x24) copies the base's descriptor except +0x18 (0x401479).
/// </summary>
public sealed class ItemFootprints
{
    public const int ResourceCount = 0x7E60;
    private const int HeaderBytes = 0x100, EntryBytes = 12, DescriptorBytes = 0x80, RowBytes = 0x102;
    private const int CategoryOffset = 0x2E, RowEntryOffset = 0x18, OverlayTargetOffset = 0x76;
    private const int RowWidthOffset = 0x1C, RowHeightOffset = 0x1D, RowBaseOffset = 0x24, RowTypeOffset = 0x80;

    private readonly byte[] _width = new byte[ResourceCount], _height = new byte[ResourceCount], _category = new byte[ResourceCount];
    private readonly ushort[] _base = new ushort[ResourceCount];

    /// <summary>The descriptor category byte (+0x2E) of <paramref name="resource"/> after all fixes; 0 = not an item type.</summary>
    public byte Category(uint resource) => resource < ResourceCount ? _category[resource] : (byte)0;

    /// <summary>The base type a Weapon.pak row names for <paramref name="resource"/> (row +0x24), or 0.</summary>
    public uint Base(uint resource) => resource < ResourceCount ? _base[resource] : 0u;

    /// <summary>Resources whose footprint comes from a Weapon.pak row (diagnostics).</summary>
    public int FromRows { get; private set; }

    /// <summary>The footprint of <paramref name="resource"/> (the item's type word); 1x1 outside the table.</summary>
    public (int Width, int Height) Get(uint resource) =>
        resource < ResourceCount ? (_width[resource], _height[resource]) : (1, 1);

    /// <summary>Inventory cell word +4 for an item anchor: 01 | width &lt;&lt; 8 | height &lt;&lt; 16.</summary>
    public uint AnchorWord(uint resource)
    {
        var (w, h) = Get(resource);
        return 1u | (uint)w << 8 | (uint)h << 16;
    }

    /// <summary>Reads PAK/Items.pak, PAK/Items00..15.pak and PAK/Weapon.pak below <paramref name="gameDirectory"/>
    /// (names matched case-insensitively). Returns null with <paramref name="error"/> when they cannot be read.</summary>
    public static ItemFootprints? Load(string gameDirectory, out string? error)
    {
        error = null;
        try
        {
            var pak = Find(gameDirectory, "PAK", folder: true) ?? throw new FileNotFoundException("no PAK folder");
            var items = File.ReadAllBytes(Find(pak, "Items.pak") ?? throw new FileNotFoundException("no Items.pak"));
            var weapon = File.ReadAllBytes(Find(pak, "Weapon.pak") ?? throw new FileNotFoundException("no Weapon.pak"));
            var overlays = new List<byte[]>();
            for (var i = 0; i < 16; i++)
                if (Find(pak, $"Items{i:D2}.pak") is { } path) overlays.Add(File.ReadAllBytes(path));
            return FromPaks(items, overlays, weapon);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            error = e.Message;
            return null;
        }
    }

    public static ItemFootprints FromPaks(ReadOnlySpan<byte> items, IReadOnlyList<byte[]> overlays, ReadOnlySpan<byte> weapon)
    {
        var category = new byte[ResourceCount];
        var entry = new ushort[ResourceCount];
        var bases = new ushort[ResourceCount];
        if (!ReadDescriptors(items, overlay: false, category, entry)) throw new InvalidDataException("Items.pak is not an item table");
        category[0xFEC] = category[0xFF2] = 0x16;                            // FUN_00404aa0, before the overlays
        category[0xFE9] = category[0xFEA] = category[0xFEB] = 0x18;
        foreach (var overlay in overlays) ReadDescriptors(overlay, overlay: true, category, entry);
        category[0xCA8] = 0x16;                                              // after the overlays

        var rows = ReadOnlySpan<byte>.Empty;
        if (weapon.Length >= HeaderBytes && weapon[3] >= 8)
        {
            var count = BinaryPrimitives.ReadUInt32LittleEndian(weapon[4..]);
            if (count > (uint)((weapon.Length - HeaderBytes) / RowBytes)) throw new InvalidDataException("Weapon.pak is truncated");
            rows = weapon.Slice(HeaderBytes, (int)count * RowBytes);
        }
        var rowCount = rows.Length / RowBytes;
        for (var i = 0; i < rowCount; i++)                                   // FUN_00406130
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(rows[(i * RowBytes + RowTypeOffset)..]);
            if (type >= ResourceCount) continue;
            entry[type] = (ushort)i;
            if (type == 0xFD5) category[type] = 6;
        }
        for (var i = 0; i < rowCount; i++)                                   // base types, 0x401479
        {
            var row = rows.Slice(i * RowBytes, RowBytes);
            var parent = BinaryPrimitives.ReadInt32LittleEndian(row[RowBaseOffset..]);
            var type = BinaryPrimitives.ReadInt32LittleEndian(row[RowTypeOffset..]);
            if (parent > 0 && parent < ResourceCount && type > 0 && type < ResourceCount) { category[type] = category[parent]; bases[type] = (ushort)parent; }
        }

        var result = new ItemFootprints();
        category.CopyTo(result._category, 0);
        bases.CopyTo(result._base, 0);
        for (var resource = 0; resource < ResourceCount; resource++)
        {
            int w, h;
            var index = entry[resource];
            var row = index < rowCount ? rows.Slice(index * RowBytes, RowBytes) : default;
            if (resource == 0xFA7) (w, h) = (2, 2);
            else if (!row.IsEmpty && row[RowWidthOffset] != 0 && row[RowHeightOffset] != 0)
            {
                (w, h) = (row[RowWidthOffset], row[RowHeightOffset]);
                result.FromRows++;
            }
            else (w, h) = ByCategory(resource, resource > 0 ? category[resource] : 0);
            result._width[resource] = (byte)w;
            result._height[resource] = (byte)h;
        }
        return result;
    }

    /// <summary>FUN_00404470 without a usable row: jump table 0x4046E0 on category - 5.</summary>
    private static (int, int) ByCategory(int resource, int category) => category switch
    {
        5 => resource is 0x6B3 or 0x6B4 or 0x6B5 or 0x6C8 or 0x6E3 or 0x6E6 ? (1, 4) : (1, 3),
        6 or 13 => (2, 3),
        16 => (1, 2),
        17 or 18 or 21 or 22 or 23 => (2, 2),
        19 or 24 => (2, 1),
        _ => (1, 1),
    };

    /// <summary>FUN_004048c0: 0x100-byte header (entry count +4), 12-byte entries [u32][u32 offset][u32 size], 0x80-byte
    /// descriptors taken when their category is nonzero; Items.pak by entry index, overlays by word +0x76.</summary>
    private static bool ReadDescriptors(ReadOnlySpan<byte> pak, bool overlay, byte[] category, ushort[] entry)
    {
        if (pak.Length < HeaderBytes || !pak[..3].SequenceEqual("ITM"u8)) return false;
        var count = BinaryPrimitives.ReadUInt32LittleEndian(pak[4..]);
        if (count > (uint)((pak.Length - HeaderBytes) / EntryBytes)) return false;
        for (var i = 0; i < (int)count; i++)
        {
            var at = pak[(HeaderBytes + i * EntryBytes)..];
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(at[4..]);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(at[8..]);
            if ((offset == 0 && size == 0) || offset > (uint)(pak.Length - DescriptorBytes)) continue;
            var descriptor = pak.Slice((int)offset, DescriptorBytes);
            if (descriptor[CategoryOffset] == 0) continue;
            var target = overlay ? BinaryPrimitives.ReadUInt32LittleEndian(descriptor[OverlayTargetOffset..]) : (uint)i;
            if (target >= ResourceCount) continue;
            category[target] = descriptor[CategoryOffset];
            entry[target] = BinaryPrimitives.ReadUInt16LittleEndian(descriptor[RowEntryOffset..]);
        }
        return true;
    }

    private static string? Find(string directory, string name, bool folder = false)
    {
        if (!Directory.Exists(directory)) return null;
        var entries = folder ? Directory.EnumerateDirectories(directory) : Directory.EnumerateFiles(directory);
        foreach (var path in entries)
            if (string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)) return path;
        return null;
    }
}
