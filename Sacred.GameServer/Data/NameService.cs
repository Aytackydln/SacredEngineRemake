using System.Buffers.Binary;
using System.Collections.Frozen;
using System.Runtime.InteropServices;
using Sacred.Assets;
using Sacred.Assets.Paks.Items;
using Sacred.Assets.Paks.Weapon;
using Sacred.Core.CombatArts;
using Sacred.Core.GameRes;
using Sacred.Core.Pak.Items;
using Sacred.Core.Pak.Weapon.Names;

namespace Sacred.GameServer.Data;

/// <summary>
/// The game's own display names for the numbers the server sees: object types (creatures and items), skills and combat arts.
/// Read once from <c>--game-dir</c> (Sacred.Assets readers): <c>scripts/&lt;language&gt;/global.res</c> for the texts, PAK/Items.pak
/// with its Items00..15.pak overlays and PAK/Weapon.pak for the type descriptors (<see cref="SacredObjectTemplateArchive"/>).
/// Lookups are dictionary reads, safe from any thread.
/// <list type="bullet">
/// <item>Object type (Items.pak index, the type word of an object state; creatures and items share it): descriptor +0x20
/// <c>ResourceId</c>, resolved like Gold 0x44A200 (<see cref="SacredEquipmentNameLookup"/>: clear bit 31 = decimal key, set = hash).
/// An object's own name word (base state native +0x3C, stream offset 37) overrides it: generated items ("Worn Greaves of Pain")
/// and named creatures carry it there, see <see cref="Object(uint, uint)"/>.</item>
/// <item>Skill id (the byte ids in a hero's skill slots): text 9399 + id (the requirement text of Sacred.Core's item
/// descriptions; checked against the saves' class skills).</item>
/// <item>Combat art id (u16 at +4 of the 22-byte art record; spells 1.., special moves 1000..): Gold cMagicEntry/cSMoveEntry name
/// resources (<see cref="SacredCombatArtDefinition.GetNameResource"/>).</item>
/// </list>
/// </summary>
public sealed class NameService
{
    private const int SkillTextBase = 9399, MaxSkill = 63;
    private readonly GameResStore _text;
    private readonly FrozenDictionary<uint, uint> _resourceOf;

    private NameService(GameResStore text, FrozenDictionary<uint, uint> resourceOf, string language)
        => (_text, _resourceOf, Language) = (text, resourceOf, language);

    /// <summary>Folder name of the text table in use ("us", "de", ...).</summary>
    public string Language { get; }

    /// <summary>Object types with a descriptor.</summary>
    public int TypeCount => _resourceOf.Count;

    /// <summary>Text of a name reference (decimal key, or bit 31 + hash); null when there is none.</summary>
    public string? Text(uint reference) => Clean(SacredEquipmentNameLookup.Resolve(_text, reference));

    /// <summary>Name of an object type, or of an object when its own name word <paramref name="instanceName"/> (native +0x3C)
    /// is known and resolves.</summary>
    public string? Object(uint type, uint instanceName = 0) =>
        (instanceName != 0 ? Text(instanceName) : null) ?? (_resourceOf.TryGetValue(type, out var resource) ? Text(resource) : null);

    /// <summary>A text of the game by its resource key (e.g. UI_REGION_SOUTHKERN); null when there is none.</summary>
    public string? TextByKey(string resourceKey) => Clean(_text.GetString(resourceKey, ""));

    /// <summary>Creature name for a creature type (e.g. 38 "Orc Warrior", 551 "Horse").</summary>
    public string? Creature(uint type, uint instanceName = 0) => Object(type, instanceName);

    /// <summary>Item name: the type's base name ("Longbow"), or the item's own generated name when its name word is given.</summary>
    public string? Item(uint type, uint instanceName = 0) => Object(type, instanceName);

    /// <summary>Skill name for a skill slot id (2 "Weapon Lore", 20 "Vampirism").</summary>
    public string? Skill(int id) => id is > 0 and <= MaxSkill ? Text((uint)(SkillTextBase + id)) : null;

    /// <summary>Combat art name for the art id of a 22-byte art record (1022 "Turn into Vampire", 60 "Blazing Disc").</summary>
    public string? CombatArt(int id) =>
        id is > 0 and <= ushort.MaxValue && SacredCombatArtDefinition.GetNameResource((ushort)id) is { } resource ? Text(resource) : null;

    private static string? Clean(string text) =>
        text.Length == 0 || text.StartsWith("+++", StringComparison.Ordinal) ? null : text.Trim();

    /// <summary>The object's own name word (native +0x3C) from a base state block that starts with 0xFEEDF00D, or 0.</summary>
    public static uint InstanceName(ReadOnlySpan<byte> body) =>
        body.Length >= 41 && BinaryPrimitives.ReadUInt32LittleEndian(body) == 0xFEEDF00D ? BinaryPrimitives.ReadUInt32LittleEndian(body[37..]) : 0;

    /// <summary>Reads the tables below <paramref name="gameDirectory"/> (names matched case-insensitively). The text table is
    /// <paramref name="language"/>'s, else "us", else the first one found. Null with <paramref name="error"/> when unreadable.</summary>
    public static NameService? Load(string gameDirectory, out string? error, string? language = null)
    {
        error = null;
        try
        {
            var scripts = Find(gameDirectory, "scripts", folder: true) ?? throw new FileNotFoundException("no scripts folder");
            var languages = Directory.EnumerateDirectories(scripts).Where(d => Find(d, "global.res") is not null)
                .OrderBy(d => d, StringComparer.OrdinalIgnoreCase).ToArray();
            var chosen = languages.FirstOrDefault(d => string.Equals(Path.GetFileName(d), language, StringComparison.OrdinalIgnoreCase))
                ?? languages.FirstOrDefault(d => string.Equals(Path.GetFileName(d), "us", StringComparison.OrdinalIgnoreCase))
                ?? languages.FirstOrDefault() ?? throw new FileNotFoundException("no scripts/<language>/global.res");
            var text = new GameResStore(SacredResUnpack.UnpackAsDictionary(Find(chosen, "global.res")!));

            var pak = Find(gameDirectory, "PAK", folder: true) ?? throw new FileNotFoundException("no PAK folder");
            var items = ItemsPakArchive.Load(File.ReadAllBytes(Find(pak, "Items.pak") ?? throw new FileNotFoundException("no Items.pak")))
                .ToDictionary(item => (uint)item.ItemIndex);
            for (var i = 0; i < 16; i++)
                if (Find(pak, $"Items{i:D2}.pak") is { } overlay)
                    foreach (var entry in ItemsPakArchive.Load(File.ReadAllBytes(overlay)))
                        if ((byte)entry.ModelDesc.Category != 0 && OverlayTarget(entry.ModelDesc) is var target && target < 0x10000)
                            items[target] = entry;
            var weapon = File.ReadAllBytes(Find(pak, "Weapon.pak") ?? throw new FileNotFoundException("no Weapon.pak"));
            var templates = SacredObjectTemplateArchive.FromPayload(items, weapon);
            var resources = templates.Items.Where(p => p.Value.ModelDesc.ResourceId != 0)
                .ToFrozenDictionary(p => p.Key, p => p.Value.ModelDesc.ResourceId);
            return new NameService(text, resources, Path.GetFileName(chosen));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            error = e.Message;
            return null;
        }
    }

    /// <summary>Items00..15.pak descriptors replace the type named by their word +0x76 (GameServer.exe FUN_004048c0).</summary>
    private static uint OverlayTarget(ItemsPakEntryModelDescLayout descriptor) =>
        BinaryPrimitives.ReadUInt32LittleEndian(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref descriptor, 1))[0x76..]);

    private static string? Find(string directory, string name, bool folder = false)
    {
        if (!Directory.Exists(directory)) return null;
        foreach (var path in folder ? Directory.EnumerateDirectories(directory) : Directory.EnumerateFiles(directory))
            if (string.Equals(Path.GetFileName(path), name, StringComparison.OrdinalIgnoreCase)) return path;
        return null;
    }
}
