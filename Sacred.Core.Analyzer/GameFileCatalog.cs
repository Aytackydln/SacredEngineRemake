namespace Sacred.Core.Analyzer;

internal static class GameFileCatalog
{
    public static IReadOnlyList<GameFileDefinition> Files { get; } =
    [
        File("pak/Items*.pak", "Item visuals and model references",
            Section("Header", "Sacred.Core.Pak.Items.ItemsPakHeaderLayout", "once"),
            Section("Entry descriptors", "Sacred.Core.Pak.Items.ItemsPakEntryInfoLayout", "EntryCount times"),
            Section("Model descriptions", "Sacred.Core.Pak.Items.ItemsPakEntryModelDescLayout", "one per populated descriptor")),
        File("pak/Weapon.pak", "Equipment definitions and damage ranges",
            Section("Header", "Sacred.Core.Pak.Weapon.WeaponPakHeaderLayout", "once"),
            Section("Equipment records", "Sacred.Core.Pak.Weapon.SacredEquipmentLayout", "EntryCount times")),
        File("pak/texture*.pak", "Texture metadata and encoded pixel payloads",
            Section("Header", "Sacred.Core.Pak.PakArchiveHeaderLayout", "once"),
            Section("Entry descriptors", "Sacred.Core.Pak.PakEntryDescriptorLayout", "EntryCount times"),
            Section("Texture headers", "Sacred.Core.Pak.Texture.TexturePakEntryHeaderLayout", "one per populated descriptor",
                "Encoded pixel payload bytes are variable-size and excluded from fixed-layout coverage.")),
        File("pak/mixed.pak", "Composite sprite groups",
            Section("Header", "Sacred.Core.Pak.PakArchiveHeaderLayout", "once"),
            Section("Entry descriptors", "Sacred.Core.Pak.PakEntryDescriptorLayout", "EntryCount times"),
            Section("Group headers", "Sacred.Core.Pak.Mixed.MixedPakGroupLayout", "one per populated descriptor"),
            Section("Sprite pieces", "Sacred.Core.Pak.Mixed.MixedPakPieceLayout", "PieceCount times per group")),
        File("pak/models*.pak", "Model archive and known payload metadata",
            Section("Header", "Sacred.Core.Pak.PakArchiveHeaderLayout", "once"),
            Section("Entry descriptors", "Sacred.Core.Pak.Models.ModelPakDescriptorLayout", "EntryCount times"),
            Section("Known payload metadata", "Sacred.Core.Pak.Models.ModelPakPayloadMetadataLayout", "one per sufficiently large payload",
                "Variable-size Granny model data is excluded from fixed-layout coverage.")),
        File("pak/Models.tmp", "Model-to-motion companion metadata",
            Section("Header", "Sacred.Core.Pak.Models.ModelsMetadataHeaderLayout", "once"),
            Section("Model records", "Sacred.Core.Pak.Models.ModelsMetadataModelLayout", "ModelCount times"),
            Section("Motion-name records", "Sacred.Core.Pak.Models.ModelsMetadataMotionLayout", "MotionCount times")),
        File("pak/tiles.pak", "Terrain tile definitions",
            Section("Header", "Sacred.Core.Pak.PakArchiveHeaderLayout", "once"),
            Section("Entry descriptors", "Sacred.Core.Pak.PakEntryDescriptorLayout", "EntryCount times"),
            Section("Known tile prefix", "Sacred.Core.Pak.Tiles.TilePakEntryLayout", "one per populated descriptor")),
        Missing("pak/Creature.pak", "Creature definitions"),
        Missing("pak/MOTIONS.PAK", "Motion archive"),
        Missing("pak/sndProfiles.pak", "Sound profiles"),
        Missing("pak/sound.pak", "Encoded sound assets"),

        File("World/Floor.pak", "Linked floor-overlay records",
            Section("Header", "Sacred.Core.Pak.PakArchiveHeaderLayout", "once"),
            Section("Entry descriptors", "Sacred.Core.Pak.PakEntryDescriptorLayout", "EntryCount times"),
            Section("Floor records", "Sacred.Core.World.FloorOverlayRecord", "one per populated descriptor")),
        File("World/Static.pak", "Linked static-world objects",
            Section("Header", "Sacred.Core.Pak.PakArchiveHeaderLayout", "once"),
            Section("Entry descriptors", "Sacred.Core.Pak.PakEntryDescriptorLayout", "EntryCount times"),
            Section("Static records", "Sacred.Core.World.StaticObjectRecord", "one per populated descriptor")),
        File("World/sectors.keyx", "Sector index and WLDX payload locations",
            Section("Header", "Sacred.Core.World.KeyxHeaderLayout", "once"),
            Section("Sector records", "Sacred.Core.World.KeyxSectorRecord", "SectorCount times")),
        File("World/sectors.wldx", "Compressed world-sector payloads",
            Section("Outdoor and indoor tiles", "Sacred.Core.World.WldxTileRecord", "once per tile in a decompressed sector"),
            Section("Post-tile header", "Sacred.Core.World.WldxPostTileHeaderLayout", "once per decompressed sector"),
            Section("Indoor-grid descriptors", "Sacred.Core.World.WldxIndoorGroupDescriptorLayout", "zero or more per sector",
                "The outer zlib stream and variable tile-array counts are excluded from fixed-layout coverage.")),

        File("bin/sets.bin", "Item-set membership",
            Section("Header", "Sacred.Core.GameBin.Sets.SacredSetHeaderLayout", "once"),
            Section("Set records", "Sacred.Core.GameBin.Sets.SacredSetEntryLayout", "SetCount times")),
        File("bin/treppe.bin", "Stairs trigger cells and zone anchors",
            Section("Cell associations", "Sacred.Core.World.Stairs.SacredStairsCellLayout", "until end of file")),
        File("bin/**/DefPos.bin", "Named script positions used by stairs and portals",
            Section("First-table header", "Sacred.Core.World.Stairs.SacredDefPosHeaderLayout", "once"),
            Section("Named positions", "Sacred.Core.World.Stairs.SacredDefPosPositionLayout", "PositionCount times",
                "Later DefPos.bin tables are not yet represented by StructLayout types.")),
        File("scripts/*/global.res", "Localized resource strings",
            Section("Header", "Sacred.Core.GameRes.GameResourceHeaderLayout", "once"),
            Section("String index", "Sacred.Core.GameRes.GameResourceIndexLayout", "StringCount times",
                "Variable-length UTF-16 string data is referenced by the index and excluded from fixed-layout coverage.")),

        Missing("bin/Balance.bin", "Balance tables"),
        Missing("bin/**/merc.bin", "Mercenary data"),
        Missing("bin/MultiStart.bin", "Multiplayer start data"),
        Missing("bin/Rust.bin", "Unmapped binary table"),
        Missing("bin/sgf.bin", "Compiled script data"),
        Missing("bin/sgq.bin", "Compiled quest data"),
        Missing("bin/sgqp.bin", "Compiled quest-pool data"),
        Missing("bin/static*.bin", "Static-object auxiliary data"),
        Missing("bin/wea.bin", "Weather data"),
        Missing("bin/World*.bin", "World configuration tables"),
        Missing("bin/wpmod.bin", "World or weapon modifier data"),
        Missing("bin/**/FunkCode.bin", "Compiled function scripts"),
        Missing("bin/**/QuestCode.bin", "Compiled quest scripts"),
        Missing("bin/**/QuestPoolCode.bin", "Compiled quest-pool scripts"),
        Missing("bin/**/StartCode.bin", "Compiled start scripts"),
        Missing("bin/**/Vectoren.bin", "Compiled script vector tables"),
        Missing("pak/Texture.tmp", "Texture companion metadata"),
        Missing("World/Triggers.pak", "World trigger definitions")
    ];

    private static GameFileDefinition File(
        string pattern,
        string description,
        params GameFileSection[] sections) => new(pattern, description, sections);

    private static GameFileDefinition Missing(string pattern, string description) =>
        new(pattern, description, []);

    private static GameFileSection Section(
        string name,
        string typeName,
        string repetition,
        string? notes = null) => new(name, typeName, repetition, notes);
}
