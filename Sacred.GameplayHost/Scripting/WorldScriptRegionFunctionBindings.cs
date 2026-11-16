using System.Collections.ObjectModel;
using System.Text;
using Sacred.Core.Executable;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Native load-time +7534 function-name map. The first index occupies
/// the low word; each duplicate replaces the high word with the latest index.</summary>
public sealed class WorldScriptRegionFunctionBindings
{
    private readonly Dictionary<string, uint> _bindings = new(WorldScriptNameComparer.Instance);
    public WorldScriptRegionFunctionBindings(IReadOnlyList<string> names, SacredRegionFunctionBindingDefinition definition)
    {
        if (definition.SignedClasses.Length != 256) throw new InvalidDataException("Expected signed-byte CRT classes.");
        var prefixes = new[] { Encoding.Latin1.GetString(definition.FirstPrefix.Span), Encoding.Latin1.GetString(definition.SecondPrefix.Span) };
        for (var index = 0; index < names.Count; index++)
        {
            var name = names[index];
            if (name.Length <= 6 || !prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))) continue;
            if (name[6] > byte.MaxValue) throw new InvalidDataException("Function names must retain original narrow bytes.");
            var signed = unchecked((sbyte)(byte)name[6]);
            if ((definition.SignedClasses.Span[signed + 128] & 4) == 0) continue;
            _bindings[name] = _bindings.TryGetValue(name, out var previous)
                ? (previous & 0xffff) | unchecked((uint)index << 16) : (uint)index;
        }
        Records = new ReadOnlyDictionary<string, uint>(_bindings);
    }
    public IReadOnlyDictionary<string, uint> Records { get; }
    public uint? Find(string name) => _bindings.TryGetValue(name, out var value) ? value : null;
}
