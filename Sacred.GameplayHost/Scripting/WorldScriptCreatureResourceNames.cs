using Sacred.Core.GameRes;
using Sacred.Core.Pak.Weapon.Names;
using Sacred.Network.Gameplay;

namespace Sacred.GameplayHost.Scripting;

/// <summary>Compared initial CreateNPC name subset: Res: operands and
/// existing local names. New local reservations and symbolic resource expressions
/// require the interpreter's original resource services.</summary>
public static class WorldScriptCreatureResourceNames
{
    public static uint Resolve(string? name, GoldScriptResourceReplica resources, GameResStore? text)
    {
        ArgumentNullException.ThrowIfNull(resources);
        if (name is null) return GoldScriptResourceLayout.UnnamedResource;
        if (name.Contains('$')) throw new InvalidDataException("CreateNPC name expressions require native variable substitution.");
        uint resource = 0;
        var decoded = name;
        if (name.StartsWith("Res:", StringComparison.OrdinalIgnoreCase))
        {
            if (text is null) throw new InvalidDataException("CreateNPC resource names require original resource text for the NON_UNIQUE gate.");
            resource = WorldScriptResourceReference.Resolve(name, text);
            var resolved = SacredEquipmentNameLookup.Resolve(text, resource);
            if (resolved.Length != 0) decoded = resolved;
        }
        if (string.Equals(decoded, "NON_UNIQUE", StringComparison.OrdinalIgnoreCase))
            return GoldScriptResourceLayout.UnnamedResource;
        if (resource is not (0 or uint.MaxValue)) return resource;
        return resources.FindLocalResource(decoded) ??
            throw new InvalidDataException($"CreateNPC local name '{decoded}' requires native resource reservation and registration.");
    }

    /// <summary>Gold 0x4836C5..0x48371D: skip zero/-1, assign an explicit map
    /// entry only when lookup differs, and always set the instance identity.</summary>
    public static GoldObjectBaseState Bind(GoldObjectBaseState actor, uint resource,
        GoldScriptResourceReplica resources, Func<uint, uint?> findObject)
    {
        if (resource is 0 or uint.MaxValue) return actor;
        if (findObject(resource) != actor.ObjectReference) resources.BindObject(resource, actor.ObjectReference);
        return actor with { Word3C = resource };
    }
}
