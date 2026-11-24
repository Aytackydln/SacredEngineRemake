using Sacred.Assets.Equipment;
using Sacred.Core.Pak.Items;
using Sacred.GameplayHost.Equipment;

namespace Sacred.GameServer.Scripting;

/// <summary>A generated item: its type (resource), the 130-byte value block and the name the generator built.</summary>
/// <remarks><see cref="Type"/> is the Items.pak resource the object is created with: the generated value block's first word, copied
/// from the Weapon.pak row's instance values (+0x80). <see cref="Row"/> is that Weapon.pak row (the simulator's selected type).</remarks>
public sealed record GeneratedItem(uint Type, uint Row, byte[] Values, SacredItemCategory Category, uint Quality, string? Name);

/// <summary>
/// Equipment generation for script rewards, on the remake's own port of the original drop code
/// (<see cref="WorldEquipmentDropSimulator"/>: category selection, type selection for level and class, rarity, values and
/// names, checked against native replays in Sacred.Network.Tests). The game data is read from <c>--game-dir</c> on first use.
/// </summary>
public sealed class LootService(string? gameDirectory)
{
    private WorldEquipmentDropSimulator? _simulator;
    private string? _error;

    public static readonly IReadOnlyDictionary<string, SacredItemCategory> Categories = new Dictionary<string, SacredItemCategory>(StringComparer.OrdinalIgnoreCase)
    {
        ["weapon"] = SacredItemCategory.Weapon, ["armor"] = SacredItemCategory.ChestArmor, ["chest"] = SacredItemCategory.ChestArmor,
        ["ring"] = SacredItemCategory.Ring, ["shield"] = SacredItemCategory.Shield, ["helmet"] = SacredItemCategory.Helmet,
        ["boots"] = SacredItemCategory.FootArmor, ["belt"] = SacredItemCategory.Belt, ["amulet"] = SacredItemCategory.Amulet,
        ["shoulders"] = SacredItemCategory.ShoulderArmor, ["arms"] = SacredItemCategory.ArmArmor, ["legs"] = SacredItemCategory.LegArmor,
        ["gloves"] = SacredItemCategory.Gloves,
    };

    public bool Available => Simulator() is not null;
    public string? Error => _error;

    private WorldEquipmentDropSimulator? Simulator()
    {
        if (_simulator is not null || _error is not null) return _simulator;
        if (gameDirectory is null) { _error = "item generation needs --game-dir"; return null; }
        try { _simulator = new WorldEquipmentDropSimulator(SacredEquipmentGenerationData.Load(gameDirectory)); }
        catch (Exception e) when (e is not OutOfMemoryException)
        { _error = $"game data in '{gameDirectory}' not readable: {e.Message}"; }
        return _simulator;
    }

    /// <summary>Rolls one piece of equipment. <paramref name="quality"/> null uses the game's own rarity weights, 0..14 forces a
    /// generation quality; <paramref name="category"/> null picks a category the way the game does for the class.</summary>
    public GeneratedItem? Roll(int level, int heroClass, int difficulty, uint? quality, SacredItemCategory? category, uint seed, out string? error)
    {
        error = null;
        if (Simulator() is not { } simulator) { error = _error; return null; }
        var request = new SacredEquipmentDropRequest((ushort)Math.Clamp(level, 1, 216), (uint)Math.Clamp(difficulty, 0, 4), seed, category,
            (uint)Math.Clamp(heroClass, 0, 9), Quality: quality is { } q ? Math.Min(q, 14) : null);
        SacredEquipmentDropResult result;
        try { result = simulator.Roll(request); }
        catch (Exception e) when (e is not OutOfMemoryException) { error = e.Message; return null; }
        if (result.SelectedType is not { } type || result.Values.IsEmpty) { error = result.UnavailableReason ?? "no item for this level and class"; return null; }
        var values = result.Values.ToArray();
        return new GeneratedItem(System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(values), type, values, result.Category, result.RolledQuality ?? quality ?? 0, result.Name?.Text);
    }
}
