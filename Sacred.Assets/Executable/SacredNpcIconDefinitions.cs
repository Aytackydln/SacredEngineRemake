using System.Buffers.Binary;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using Sacred.Core.Pak.Creature;

namespace Sacred.Assets.Executable;

/// <summary>Original overhead texture-cache switch and simulation-state lookup table.
/// Reads the selected executable; contains no item/texture index substitutions.</summary>
public sealed class SacredNpcIconDefinitions
{
    private readonly IReadOnlyDictionary<uint, string> _textures;
    private readonly IReadOnlyDictionary<uint, uint> _colors;
    private SacredNpcIconDefinitions(Dictionary<uint, string> textures, Dictionary<uint, uint> colors, int[] states)
    { _textures = textures; _colors = colors; SimulationIcons = Array.AsReadOnly(states); }
    public IReadOnlyList<int> SimulationIcons { get; }
    public IEnumerable<string> TextureNames => _textures.Values.Distinct(StringComparer.OrdinalIgnoreCase);
    public string? Texture(uint selector)
    {
        // Gold 0x4090D0 explicitly skips these selectors before resolving a texture.
        if (selector is 8 or 13 or 126) return null;
        return _textures.TryGetValue(selector, out var name) ? name : _textures[127];
    }
    /// <summary>Gold's vertex diffuse color in ARGB order. Quest/service art is grayscale;
    /// the executable supplies its yellow, completed-dialog gray or simulation white tint.</summary>
    public uint ColorArgb(uint selector) => _colors.GetValueOrDefault(selector, _colors[127]);
    public static SacredNpcIconDefinitions Load(string gameDirectory)
    {
        Exception? failure = null;
        foreach (var filename in new[] { "Sacred.exe", "pureHD.exe" })
        {
            var path = Path.Combine(gameDirectory, filename);
            if (!File.Exists(path)) continue;
            try { return Read(File.ReadAllBytes(path)); }
            catch (InvalidDataException error) { failure = error; }
        }
        throw new InvalidDataException("Native NPC overhead definitions unavailable in this installation.", failure);
    }
    public static SacredNpcIconDefinitions Read(byte[] image)
    {
        using var pe = new PEReader(new MemoryStream(image, writable: false));
        var headers = pe.PEHeaders;
        var imageBase = headers.PEHeader?.ImageBase ?? throw new InvalidDataException("Missing executable header.");
        var names = new Dictionary<uint, string>();
        var text = Encoding.Latin1.GetString(image);
        foreach (Match match in Regex.Matches(text, @"(?:npc_dialog_[a-z0-9_]+|sim_[a-z0-9_]+)\.tga\x00", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            foreach (var section in headers.SectionHeaders)
            {
                if (match.Index < section.PointerToRawData || match.Index >= section.PointerToRawData + section.SizeOfRawData) continue;
                var address = checked((uint)(imageBase + (ulong)(section.VirtualAddress + match.Index - section.PointerToRawData)));
                names[address] = match.Value[..^1];
                break;
            }
        }
        var caches = new Dictionary<uint, string>();
        var cacheColors = new Dictionary<uint, uint>();
        for (var offset = 0; offset <= image.Length - 22; offset++)
        {
            // push texture; mov ecx,ebp; call loadTexture; mov [cache],eax.
            var code = image.AsSpan(offset, 22);
            if (code[0] != 0x68 || code[5] != 0x8B || code[6] != 0xCD || code[7] != 0xE8 || code[12] != 0xA3) continue;
            if (!names.TryGetValue(BinaryPrimitives.ReadUInt32LittleEndian(code[1..]), out var name)) continue;
            // The case sets its diffuse immediately after the lazy texture-cache store:
            // mov ebp,argb or or ebp,-1. Read that value rather than assigning role colors.
            uint color;
            if (code[17] == 0xBD) color = BinaryPrimitives.ReadUInt32LittleEndian(code[18..]);
            else if (code[17] == 0x83 && code[18] == 0xCD && code[19] == 0xFF) color = uint.MaxValue;
            else continue;
            var cache = BinaryPrimitives.ReadUInt32LittleEndian(code[13..]);
            caches[cache] = name;
            cacheColors[cache] = color;
        }
        if (caches.Count < 24) throw new InvalidDataException("Native NPC overhead texture switch not found.");
        var first = caches.Keys.Min();
        var textures = new Dictionary<uint, string>();
        var colors = new Dictionary<uint, uint>();
        foreach (var pair in caches)
        {
            var distance = pair.Key - first;
            if (distance % 4 != 0 || distance / 4 > 131) continue;
            textures[distance / 4] = pair.Value;
            colors[distance / 4] = cacheColors[pair.Key];
        }
        if (!textures.ContainsKey(0) || !textures.ContainsKey(127) || !textures.ContainsKey(0x49))
            throw new InvalidDataException("Incomplete native NPC overhead texture switch.");
        // The state table is independent of cache addresses and is preserved in the protected image.
        ReadOnlySpan<byte> prefix = [0,0,0,0,90,0,0,0,64,0,0,0,72,0,0,0,73,0,0,0,74,0,0,0,75,0,0,0,76,0,0,0];
        var table = image.AsSpan().IndexOf(prefix);
        if (table < 0 || table > image.Length - SacredNpcOverheadState.SimulationStateCount * 4)
            throw new InvalidDataException("Native NPC simulation icon table not found.");
        var states = new int[SacredNpcOverheadState.SimulationStateCount];
        for (var index = 0; index < states.Length; index++)
        {
            states[index] = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(table + index * 4));
            if (states[index] is < 0 or > 90) throw new InvalidDataException("Invalid NPC simulation icon table.");
        }
        return new(textures, colors, states);
    }
}
