using System.Collections.Frozen;
using System.Globalization;
using System.Numerics;
using Sacred.Assets.GameBin;
using Sacred.Core.GameBin.Scripts;
using Sacred.Core.Pak.Items;
using Sacred.Core.World.Portals;
using Sacred.Core.World.Sector;
using Sacred.Core.World.Stairs;
using Sacred.Particles;

namespace Sacred.World.Portals;

/// <summary>Persistent sector-script effects and startup portal triggers. Does not execute
/// quest callbacks or infer portal links from names, distances or item IDs.</summary>
public sealed class WorldPortalScriptIndex
{
    public static WorldPortalScriptIndex Empty { get; } = new([], []);
    private readonly FrozenDictionary<SectorCoord, IReadOnlyList<SacredPortalBillboard>> _bySector;
    public IReadOnlyList<SacredPortalBillboard> Billboards { get; }
    public IReadOnlyList<SacredPortalTrigger> Triggers { get; }

    private WorldPortalScriptIndex(IReadOnlyList<SacredPortalBillboard> billboards,
        IReadOnlyList<SacredPortalTrigger> triggers)
    {
        Billboards = billboards;
        Triggers = triggers;
        _bySector = billboards.GroupBy(p => new SectorCoord(p.TileX / Sector.TileCount, p.TileY / Sector.TileCount))
            .ToFrozenDictionary(g => g.Key, g => (IReadOnlyList<SacredPortalBillboard>)g.ToArray());
    }

    public IReadOnlyList<SacredPortalBillboard> GetBillboards(SectorCoord sector) =>
        _bySector.GetValueOrDefault(sector, Array.Empty<SacredPortalBillboard>());

    public static WorldPortalScriptIndex Load(string scriptDirectory, IEnumerable<ItemsPakEntry> items)
    {
        var functionsPath = Path.Combine(scriptDirectory, "Vectoren.bin");
        if (!File.Exists(functionsPath)) return Empty;
        var code = File.ReadAllBytes(Path.Combine(scriptDirectory, "FunkCode.bin"));
        var functions = SacredScriptFunctionReader.Read(File.ReadAllBytes(functionsPath), code.Length);
        var definitions = SacredDefPosPosition.ReadMany(File.ReadAllBytes(Path.Combine(scriptDirectory, "DefPos.bin")))
            .ToDictionary(p => p.Name, p => new SacredScriptPosition(p.X, p.Y, p.Z), StringComparer.OrdinalIgnoreCase);
        // The type comes from the native effect's Items.pak name, not an invented ID mapping.
        var portalTypes = items.Where(item => item.ModelDesc.Category == SacredItemCategory.Effect &&
                item.ModelName.Equals("FX_STARGATE", StringComparison.OrdinalIgnoreCase))
            .Select(item => (uint)item.ItemIndex).ToHashSet();
        var billboards = ReadBillboards(functions, code, portalTypes);
        var triggers = ReadTriggers(File.ReadAllBytes(Path.Combine(scriptDirectory, "StartCode.bin")),
            functions, code, definitions);
        var result = new WorldPortalScriptIndex(billboards, triggers);
        Console.WriteLine($"Default portals loaded: {billboards.Count} sector-script billboards, {triggers.Count} startup teleport triggers.");
        return result;
    }

    private static List<SacredPortalBillboard> ReadBillboards(IReadOnlyList<SacredScriptFunction> functions,
        byte[] code, HashSet<uint> portalTypes)
    {
        var result = new List<SacredPortalBillboard>();
        var units = SacredParticleCatalogue.LoadEmbedded().WorldUnitsPerTile;
        foreach (var function in functions)
        {
            if (function.Deleted != 0 || function.Quest != -1 || function.ByteLength == 0 ||
                !function.Name.StartsWith("Sector", StringComparison.OrdinalIgnoreCase) ||
                !function.Name.EndsWith("Enter", StringComparison.OrdinalIgnoreCase)) continue;
            var commands = SacredCompiledScriptReader.Read(code.AsMemory(function.Start, function.ByteLength)).ToArray();
            if (commands.Any(command => command.Opcode == SacredScriptPortalOpcodes.If)) continue;
            foreach (var command in commands)
            {
                if (!SacredScriptCreateObjectReader.TryRead(command, out var creation, out _) ||
                    !portalTypes.Contains(creation.TypeId) || creation.TilePosition is not { } tile) continue;
                // Gold dispatches each sector's Enter function using this generated name.
                var name = string.Create(CultureInfo.InvariantCulture,
                    $"Sector{tile.X / Sector.TileCount}{tile.Y / Sector.TileCount:D3}Enter");
                if (!function.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
                var position = creation.WorldPosition is { } world
                    ? new Vector2(world.X / units, world.Y / units)
                    : new Vector2(tile.X, tile.Y);
                result.Add(new(function.Start + command.FileOffset, creation.TypeId, position, tile.X, tile.Y,
                    (byte)(tile.Z is >= 0 and <= 16 ? tile.Z : 0), creation.HeightOffset ?? 0));
            }
        }
        return result;
    }

    private static List<SacredPortalTrigger> ReadTriggers(byte[] startup,
        IReadOnlyList<SacredScriptFunction> functions, byte[] code, Dictionary<string, SacredScriptPosition> definitions)
    {
        var rectangles = new Dictionary<string, (SacredScriptPosition First, SacredScriptPosition Last)>(StringComparer.OrdinalIgnoreCase);
        var callbacks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var markers = new List<Vector2>();
        var conditionalDepth = 0;
        foreach (var command in SacredCompiledScriptReader.Read(startup))
        {
            if (command.Opcode == SacredScriptPortalOpcodes.If) { conditionalDepth++; continue; }
            if (command.Opcode == SacredScriptPortalOpcodes.EndBlock && conditionalDepth > 0) { conditionalDepth--; continue; }
            if (conditionalDepth > 0) continue;
            if (command.Opcode is not (SacredScriptPortalOpcodes.DefinePosition or
                SacredScriptPortalOpcodes.SetBaseTrigger or SacredScriptPortalOpcodes.DeleteBaseTrigger or
                SacredScriptPortalOpcodes.OnMoveOver or SacredScriptPortalOpcodes.SetMapIcon)) continue;
            var args = SacredPortalScriptArguments.Read(command, definitions);
            if (args is null) continue;
            switch (command.Opcode)
            {
                case SacredScriptPortalOpcodes.DefinePosition when args.Name is { } name && args.Integers.Count >= 2:
                    definitions[name] = new(args.Integers[0], args.Integers[1], args.Integers.Count >= 3 ? args.Integers[2] : 0);
                    break;
                case SacredScriptPortalOpcodes.SetBaseTrigger when args.Name is { } name && args.Positions.Count > 0:
                    var first = args.Positions.GetValueOrDefault((byte)0x0C, args.Positions.Values.First());
                    rectangles[name] = (first, args.Positions.GetValueOrDefault((byte)0x0D, first));
                    break;
                case SacredScriptPortalOpcodes.DeleteBaseTrigger when args.Name is { } name:
                    rectangles.Remove(name);
                    break;
                case SacredScriptPortalOpcodes.OnMoveOver when args.Name is { } name && args.Integers.Count == 1:
                    callbacks[name] = args.Integers[0];
                    break;
                case SacredScriptPortalOpcodes.SetMapIcon when args.Integers.Count == 3 && args.Integers[2] == SacredScriptPortalOpcodes.PortalMapIcon:
                    markers.Add(new(args.Integers[0], args.Integers[1]));
                    break;
            }
        }
        var result = new List<SacredPortalTrigger>();
        foreach (var (name, rectangle) in rectangles)
        {
            if (!callbacks.TryGetValue(name, out var index) || (uint)index >= functions.Count) continue;
            var function = functions[index];
            if (function.Deleted != 0) continue;
            var commands = SacredCompiledScriptReader.Read(code.AsMemory(function.Start, function.ByteLength)).ToArray();
            // A conditional or compound callback cannot establish a default-open link.
            if (commands.Length != 1 || commands[0].Opcode != SacredScriptPortalOpcodes.Teleport) continue;
            var args = SacredPortalScriptArguments.Read(commands[0], definitions);
            if (args is null || !args.Positions.TryGetValue(4, out var destination)) continue;
            var trigger = new SacredPortalTrigger(name, Math.Min(rectangle.First.X, rectangle.Last.X),
                Math.Min(rectangle.First.Y, rectangle.Last.Y), Math.Max(rectangle.First.X, rectangle.Last.X),
                Math.Max(rectangle.First.Y, rectangle.Last.Y),
                (byte)(rectangle.Last.Z is >= 0 and <= 16 ? rectangle.Last.Z : 0), destination);
            if (markers.Any(marker => trigger.Contains(marker, trigger.SurfaceLevel))) result.Add(trigger);
        }
        return result;
    }
}
