using Sacred.Core.GameRes;
using System.Globalization;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>CreateObj identity resolution. Local aliases are invalidated before fresh
/// reservation; numeric/localized identities bypass that pass. Binding always overwrites.</summary>
public sealed class WorldScriptItemResources(GoldScriptResourceReplica replica, GameResStore? text)
{
    public GoldScriptResourceReplica Replica { get; } = replica;
    /// <summary>Later CreateObj names retain the operand decoder's resource;
    /// unlike the primary name, plain secondary names reserve no local alias.</summary>
    public uint ResolveSecondary(string? name)
    {
        if (name is null) return GoldScriptResourceLayout.UnnamedResource;
        if (name.Contains('$')) throw new InvalidDataException("CreateObj secondary-name expressions require native resolution.");
        if (!name.StartsWith("Res:", StringComparison.OrdinalIgnoreCase)) return 0;
        return WorldScriptResourceReference.Resolve(name, text ??
            throw new InvalidDataException("CreateObj secondary resource requires original resource text."));
    }
    public uint Resolve(string? name)
    {
        if (name is null) return GoldScriptResourceLayout.UnnamedResource;
        if (name.Contains('$')) throw new InvalidDataException("CreateObj name expressions require native resolution.");
        uint resource = 0;
        var decoded = name;
        if (name.StartsWith("Res:", StringComparison.OrdinalIgnoreCase))
        {
            if (text is null) throw new InvalidDataException("CreateObj resources require original resource text.");
            resource = WorldScriptResourceReference.Resolve(name, text);
            // The operand decoder queries numeric text even for zero/FFFFFFFF;
            // item display-name lookup has different sentinel/high-bit rules.
            var token=name.AsSpan(4);
            var textKey=!token.IsEmpty && token[0] is >= '0' and <= '9'
                ? SacredResourceHash.Compute(resource.ToString(CultureInfo.InvariantCulture))
                : resource & 0x7FFFFFFF;
            var localized = text.Strings.GetValueOrDefault(textKey, "");
            if (localized.Length != 0) decoded = localized;
        }
        if (resource is 0 or uint.MaxValue)
        {
            Replica.InvalidateLocalName(decoded);
            resource = Replica.ReserveLocalResource(decoded, true);
        }
        if (WorldScriptNameComparer.Instance.Equals(decoded,"NON_UNIQUE")) return GoldScriptResourceLayout.UnnamedResource;
        return resource;
    }
    public GoldObjectBaseState Bind(GoldObjectBaseState state, uint resource)
    {
        if (resource == GoldScriptResourceLayout.UnnamedResource) return state;
        Replica.BindObject(resource,state.ObjectReference);
        return state with { Word3C = resource };
    }
}
