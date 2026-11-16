using Sacred.Core.Executable;

namespace Sacred.GameplayHost.Networking;

/// <summary>Complete 0x81E5B0 getter control flow. Neither negative/overflowed
/// parsed values nor a leading NUL imply missing data. No port normalization.</summary>
public static class WorldListenPort
{
    public static uint Read(SacredListenPortDefinition definition, WorldListenPortServices services)
    {
        ArgumentNullException.ThrowIfNull(definition); ArgumentNullException.ThrowIfNull(services);
        var value = services.ReadValue(definition.ReadKey);
        if (!value.IsEmpty) return unchecked((uint)services.ParseDecimal(value));
        services.WriteDefault(definition.DefaultKey, definition.DefaultWriteValue);
        return definition.DefaultReturnValue;
    }

    /// <summary>Original 0x7EE4F1..0x7EE509 assignment/log order in the captured
    /// session-settings importer. The complete importer is a separate boundary.</summary>
    public static void LoadController(WorldControlMemory controller, SacredListenPortDefinition definition,
        WorldListenPortServices services, Action<uint> log)
    {
        ArgumentNullException.ThrowIfNull(controller); ArgumentNullException.ThrowIfNull(log);
        var value = Read(definition, services);
        controller.WriteWord(0x40, value);
        log(value);
    }
}
