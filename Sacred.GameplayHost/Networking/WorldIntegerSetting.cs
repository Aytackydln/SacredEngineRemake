using Sacred.Core.Executable;

namespace Sacred.GameplayHost.Networking;

public static class WorldIntegerSetting
{
    /// <summary>Compared complete lobby-port/speed/ASE-port getter decisions.
    /// Stored byte length selects the missing path, independently of C-string
    /// termination or numeric value; fallback never performs a second read.</summary>
    public static uint Read(SacredIntegerSettingDefinition definition,
        Func<ReadOnlyMemory<byte>, ReadOnlyMemory<byte>> readValue,
        Action<ReadOnlyMemory<byte>, uint> writeDefault, Func<ReadOnlyMemory<byte>, int> parseDecimal)
    {
        var value = readValue(definition.ReadKey);
        if (!value.IsEmpty) return unchecked((uint)parseDecimal(value));
        if (definition.DefaultWriteValue is { } write) writeDefault(definition.DefaultKey, write);
        return definition.DefaultReturnValue;
    }
}
