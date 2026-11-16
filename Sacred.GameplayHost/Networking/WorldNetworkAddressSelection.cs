namespace Sacred.GameplayHost.Networking;

/// <summary>Original 0x7EE178..0x7EE291 IP selection. Count is captured after
/// logging; only three inline address slots are readable. Later loop candidates
/// are native zero, rather than inferred collection members.</summary>
public static class WorldNetworkAddressSelection
{
    public static void Apply(WorldNetworkAddressServices services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var manager = services.Manager;
        var configured = services.ReadConfiguredAddress();
        if (configured.IsEmpty) return;
        var parsed = services.ParseAddress(configured);
        services.Log("configured", configured);
        if (parsed != manager.ReadWord(0x10))
        {
            var count = manager.ReadWord(0);
            for (uint index = 0; index < count; index++)
            {
                var candidate = index < 3 ? manager.ReadWord(4 + index * 4) : 0;
                if (candidate != parsed) continue;
                manager.WriteWord(0x10, parsed);
                manager.WriteWord(0x14, parsed);
                services.WriteString(0x18, configured);
                services.WriteString(0x24, configured);
                services.Log("selected", ReadOnlyMemory<byte>.Empty);
                break;
            }
        }
        services.Log("complete", ReadOnlyMemory<byte>.Empty);
    }

    /// <summary>Complete 0x7F5F60. This selected IPv4 word is copied to locally
    /// staged roster record+0; it is distinct from controller+0x14's player slot.</summary>
    public static uint ReadSelected(WorldControlMemory manager)
    {
        ArgumentNullException.ThrowIfNull(manager);
        return manager.ReadWord(0x14);
    }
}
